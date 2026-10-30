using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Strategy: track storage identities and stable complete-storage owners.
public sealed class StackStorageTypeAnalyzer(
    ITypeRepository types,
    IFieldRepository fields,
    IMethodRepository methods,
    IValueLayoutProvider layouts,
    IStackStorageContinuationAnalyzer continuations) : IStackStorageTypeAnalyzer
{
    private readonly ITypeRepository _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IValueLayoutProvider _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IStackStorageContinuationAnalyzer _continuations =
        continuations ?? throw new ArgumentNullException(nameof(continuations));

    public StackStorageAnalysis Analyze(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var controlFlow = method.ControlFlow;
        var graph = controlFlow.Graph;
        var producers = new Dictionary<int, StorageIdentity>();
        var candidates = false;
        var references = new Dictionary<CliTypeIdentity, bool>();
        var unstableArgumentPointers = method.Body.Instructions
            .Where(instruction => controlFlow.InstructionEntryStacks.ContainsKey(instruction.Offset) &&
                instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadArgumentAddress)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToImmutableHashSet();
        foreach (var instruction in method.Body.Instructions)
        {
            if (!controlFlow.InstructionEntryStacks.ContainsKey(instruction.Offset))
            {
                continue;
            }
            // Arithmetic preserves pointer category, not the original pointee.
            // It cannot certify absence of references for the screening fast path.
            candidates |= instruction.Operation is CilOperation.Add or CilOperation.Subtract &&
                controlFlow.InstructionEntryStacks[instruction.Offset].Contains(CliValueKind.ManagedAddress);
            var type = ResolveProducedType(method, instruction);
            if (type?.StackKind is not (CliValueKind.ValueType or CliValueKind.ManagedAddress))
            {
                continue;
            }
            if (type.ContainsGenericParameters)
            {
                throw Invalid(method, instruction, "stack storage type is not closed");
            }
            var storage = type.StackKind == CliValueKind.ManagedAddress ? type.ElementType! : type;
            if (!references.TryGetValue(storage, out var containsReferences))
            {
                containsReferences = storage.StackKind is CliValueKind.ManagedReference or CliValueKind.ManagedAddress ||
                    storage.StackKind == CliValueKind.ValueType &&
                    !_layouts.GetValueLayout(storage).ReferenceOffsets.IsEmpty;
                references.Add(storage, containsReferences);
            }
            candidates |= containsReferences;
            var backingIsKnown = storage.StackKind != CliValueKind.ManagedAddress &&
                (instruction.Operation == CilOperation.LoadLocalAddress ||
                    instruction.Operation == CilOperation.LoadArgumentAddress &&
                    (method.Method.Definition.IsStatic || ((CilOperand.Index)instruction.Operand).Value != 0) &&
                    (!containsReferences || storage.StackKind == CliValueKind.ValueType));
            var stableArgumentPointer = instruction.Operation == CilOperation.LoadArgument &&
                type.StackKind == CliValueKind.ManagedAddress && storage.StackKind != CliValueKind.ManagedAddress &&
                !unstableArgumentPointers.Contains(((CilOperand.Index)instruction.Operand).Value);
            backingIsKnown |= stableArgumentPointer;
            ImmutableHashSet<StackStorageOwner> owners = backingIsKnown && containsReferences
                ? [new(instruction.Operation != CilOperation.LoadLocalAddress,
                    ((CilOperand.Index)instruction.Operand).Value, stableArgumentPointer ? type : storage)] : [];
            producers.Add(instruction.Offset, new(type, false, backingIsKnown, owners,
                backingIsKnown || storage.StackKind == CliValueKind.ManagedAddress ? [] : [storage],
                storage.StackKind != CliValueKind.ManagedAddress));
        }
        if (!candidates)
        {
            return StackStorageAnalysis.Empty;
        }

        var initialLocals = method.Body.LocalSignatureTypes.Select(type =>
        {
            var closed = type.Substitute(method.Method.DeclaringType.TypeArguments, method.Method.MethodArguments);
            while (closed.Shape == CliTypeShape.Modified) closed = closed.ElementType!;
            return closed.StackKind == CliValueKind.ManagedAddress
                ? new StorageIdentity(closed, false, false, [], [closed.ElementType!],
                    closed.ElementType!.StackKind != CliValueKind.ManagedAddress) : default;
        }).ToImmutableArray();
        var flow = _continuations.Analyze(method);
        var entries = new Dictionary<int, StorageState>();
        var pending = new Queue<int>();
        var result = ImmutableDictionary.CreateBuilder<int, ImmutableArray<StackStorageType>>();
        var localResult = ImmutableDictionary.CreateBuilder<int, ImmutableArray<StackStorageType>>();
        var transfers = ImmutableDictionary.CreateBuilder<int, StackStorageTransfer>();
        Merge(graph.Entry.Index, new([], initialLocals));
        var detachedRegionsSeeded = false;
        while (true)
        {
            if (!pending.TryDequeue(out var blockIndex))
            {
                if (detachedRegionsSeeded) break;
                detachedRegionsSeeded = true;
                foreach (var region in method.Body.ExceptionRegions)
                {
                    SeedDetached(region.HandlerOffset);
                    if (region.FilterOffset is int filterOffset) SeedDetached(filterOffset);
                }
                continue;
            }
            var block = graph.GetBlock(blockIndex);
            var stack = entries[blockIndex].Stack.ToList();
            var locals = entries[blockIndex].Locals.ToBuilder();
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var instruction = block.Instructions[index];
                result[instruction.Offset] = [.. stack.Select(Describe)];
                localResult[instruction.Offset] = [.. locals.Select(Describe)];
                if (CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                {
                    foreach (var successor in flow.Exceptional[blockIndex])
                    {
                        Merge(successor.Block, new(
                            [.. controlFlow.EntryStacks[successor.Block].Select(_ => default(StorageIdentity))],
                            locals.ToImmutable()));
                    }
                }
                if (instruction.Operation == CilOperation.Duplicate)
                {
                    transfers[instruction.Offset] = new(new(StackStorageLocationKind.EvaluationStack, stack.Count),
                        stack[^1].Captures.ToImmutableDictionary(type => type,
                            _ => new StackStorageLocation(StackStorageLocationKind.EvaluationStack, stack.Count - 1)), []);
                    stack.Add(stack[^1]);
                    continue;
                }

                if (instruction.Operation == CilOperation.StoreLocal &&
                    method.Body.Locals[((CilOperand.Index)instruction.Operand).Value] == CliValueKind.ManagedAddress)
                {
                    var local = ((CilOperand.Index)instruction.Operand).Value;
                    // An unmanaged pointer stored into a byref local, as `ref *pointer` stores one,
                    // addresses memory the collector does not manage: no backing to keep alive.
                    locals[local] = controlFlow.InstructionEntryStacks[instruction.Offset][^1] == CliValueKind.NativeInt
                        ? new StorageIdentity(initialLocals[local].Type, false, true, [], [], true)
                        : stack[^1];
                    transfers[instruction.Offset] = new(new(StackStorageLocationKind.Local, local),
                        locals[local].Captures.ToImmutableDictionary(type => type,
                            _ => new StackStorageLocation(StackStorageLocationKind.EvaluationStack, stack.Count - 1)), []);
                }

                var outgoing = index + 1 < block.Instructions.Length
                    ? controlFlow.InstructionEntryStacks[block.Instructions[index + 1].Offset]
                    : graph.Successors[blockIndex].IsEmpty
                        ? []
                        : controlFlow.EntryStacks[graph.Successors[blockIndex][0]];
                var produced = ProducedCount(instruction);
                var identity = producers.GetValueOrDefault(instruction.Offset);
                StackStorageLocation? copySource = null;
                if (instruction.Operation == CilOperation.LoadLocal &&
                    method.Body.Locals[((CilOperand.Index)instruction.Operand).Value] == CliValueKind.ManagedAddress)
                {
                    var local = ((CilOperand.Index)instruction.Operand).Value;
                    identity = locals[local];
                    copySource = new(StackStorageLocationKind.Local, local);
                }
                if (instruction.Operation == CilOperation.LoadFieldAddress)
                {
                    copySource = new(StackStorageLocationKind.EvaluationStack, stack.Count - 1);
                    identity = identity with
                    {
                        BackingCovered = stack[^1].BackingCovered,
                        BackingOwners = stack[^1].Owners,
                        BackingCaptures = stack[^1].Captures,
                        CompleteBackingKnown = stack[^1].CompleteBackingKnown,
                    };
                }
                if (instruction.Operation is CilOperation.Add or CilOperation.Subtract &&
                    !outgoing.IsEmpty && outgoing[^1] == CliValueKind.ManagedAddress)
                {
                    copySource = new(StackStorageLocationKind.EvaluationStack,
                        controlFlow.InstructionEntryStacks[instruction.Offset][^2] == CliValueKind.ManagedAddress
                            ? stack.Count - 2 : stack.Count - 1);
                    var address = stack[copySource.Value.Index];
                    identity = new(null, true, address.BackingCovered, address.Owners, address.Captures,
                        address.CompleteBackingKnown);
                }
                var preserved = outgoing.Length - produced;
                stack.RemoveRange(preserved, stack.Count - preserved);
                if (produced != 0)
                {
                    transfers[instruction.Offset] = new(new(StackStorageLocationKind.EvaluationStack, preserved),
                        copySource is { } source
                            ? identity.Captures.ToImmutableDictionary(type => type, _ => source)
                            : ImmutableDictionary<CliTypeIdentity, StackStorageLocation>.Empty,
                        copySource.HasValue ? [] : identity.Captures);
                    stack.Add(identity with
                    {
                        Unresolved = identity.Unresolved || identity.Type is null &&
                        outgoing[^1] is CliValueKind.ValueType or CliValueKind.ManagedAddress
                    });
                }
            }
            foreach (var successor in flow.Normal[blockIndex])
            {
                Merge(successor.Block, new(successor.ResetStack
                    ? [.. controlFlow.EntryStacks[successor.Block].Select(_ => default(StorageIdentity))]
                    : [.. stack], locals.ToImmutable()));
            }
        }
        return new(result.ToImmutable(), transfers.ToImmutable()) { Locals = localResult.ToImmutable() };

        void SeedDetached(int offset)
        {
            var block = graph.GetBlockAtOffset(offset).Index;
            if (graph.ReachableBlocks.Contains(block) && !entries.ContainsKey(block))
            {
                Merge(block, new([.. controlFlow.EntryStacks[block].Select(_ => default(StorageIdentity))], initialLocals));
            }
        }

        void Merge(int block, StorageState incoming)
        {
            if (entries.TryGetValue(block, out var existing))
            {
                var changed = false;
                var stack = Join(existing.Stack, incoming.Stack);
                var locals = Join(existing.Locals, incoming.Locals);
                if (changed)
                {
                    entries[block] = new(stack, locals);
                    pending.Enqueue(block);
                }
                return;

                ImmutableArray<StorageIdentity> Join(ImmutableArray<StorageIdentity> first,
                    ImmutableArray<StorageIdentity> second)
                {
                    var merged = first.ToBuilder();
                    for (var slot = 0; slot < first.Length; slot++)
                    {
                        if (first[slot].Type is { } known && second[slot].Type is { } other && !known.Equals(other))
                        {
                            throw Invalid(method, graph.GetBlock(block).Instructions[0],
                                "incompatible stack storage identities at control-flow merge");
                        }
                        var joined = new StorageIdentity(first[slot].Type ?? second[slot].Type,
                            first[slot].Unresolved || second[slot].Unresolved,
                            first[slot].BackingCovered && second[slot].BackingCovered,
                            first[slot].Owners.Union(second[slot].Owners),
                            first[slot].Captures.Union(second[slot].Captures),
                            first[slot].CompleteBackingKnown && second[slot].CompleteBackingKnown);
                        if (joined.Type != first[slot].Type || joined.Unresolved != first[slot].Unresolved ||
                            joined.BackingCovered != first[slot].BackingCovered ||
                            joined.CompleteBackingKnown != first[slot].CompleteBackingKnown ||
                            !joined.Owners.SetEquals(first[slot].Owners) || !joined.Captures.SetEquals(first[slot].Captures))
                        {
                            merged[slot] = joined;
                            changed = true;
                        }
                    }
                    return merged.ToImmutable();
                }
            }
            entries.Add(block, incoming);
            pending.Enqueue(block);
        }
    }

    private static StackStorageType Describe(StorageIdentity slot) =>
        new(slot.Unresolved ? null : slot.Type, slot.BackingCovered)
        {
            BackingOwners = slot.Owners,
            CapturedBackings = slot.Captures,
            CompleteBackingKnown = slot.CompleteBackingKnown,
        };

    private CliTypeIdentity? ResolveProducedType(ManagedMethodBody method, CilInstruction instruction)
    {
        var body = method.Body;
        var instance = method.Method;
        CliTypeIdentity? type;
        switch (instruction.Operation)
        {
            case CilOperation.LoadArgument:
            case CilOperation.LoadArgumentAddress:
                var argument = ((CilOperand.Index)instruction.Operand).Value;
                type = !instance.Definition.IsStatic && argument == 0
                    ? instance.DeclaringType.IsValueType
                        ? CliTypeIdentity.ManagedByReference(instance.DeclaringType)
                        : instance.DeclaringType
                    : instance.Signature.ParameterSignatureTypes[argument - (instance.Definition.IsStatic ? 0 : 1)];
                break;
            case CilOperation.LoadLocal:
            case CilOperation.LoadLocalAddress:
                type = body.LocalSignatureTypes[((CilOperand.Index)instruction.Operand).Value];
                break;
            case CilOperation.LoadField:
            case CilOperation.LoadFieldAddress:
            case CilOperation.LoadStaticField:
            case CilOperation.LoadStaticFieldAddress:
                type = instruction.Operand is CilOperand.FieldInstance field
                    ? field.Value.FieldType
                    : _fields.GetField(((CilOperand.Entity)instruction.Operand).Key).SignatureType;
                break;
            case CilOperation.Call:
            case CilOperation.CallVirtual:
            case CilOperation.NewObject:
                var target = instruction.Operand is CilOperand.MethodInstance called
                    ? called.Value
                    : ResolveMethod(((CilOperand.Entity)instruction.Operand).Key);
                type = instruction.Operation == CilOperation.NewObject
                    ? target.DeclaringType : target.Signature.ReturnSignatureType;
                break;
            case CilOperation.CallIndirect:
                type = ((CilOperand.CallSite)instruction.Operand).Signature.ReturnSignatureType;
                break;
            case CilOperation.LoadObject:
            case CilOperation.DefaultValue:
            case CilOperation.Unbox:
            case CilOperation.UnboxAny:
            case CilOperation.LoadArrayElement:
            case CilOperation.LoadArrayElementAddress:
            case CilOperation.CompareExchange:
                type = ((CilOperand.TypeIdentity)instruction.Operand).Value;
                break;
            case CilOperation.CreateReadOnlySpanFromRva:
                type = ((CilOperand.ReadOnlySpanData)instruction.Operand).SpanType;
                break;
            case CilOperation.LoadRectangularArrayElement:
            case CilOperation.LoadRectangularArrayElementAddress:
                type = ((CilOperand.TypeIdentity)instruction.Operand).Value.ElementType!;
                break;
            default:
                return null;
        }
        type = type.Substitute(instance.DeclaringType.TypeArguments, instance.MethodArguments);
        while (type.Shape == CliTypeShape.Modified)
        {
            type = type.ElementType!;
        }
        return instruction.Operation is CilOperation.LoadArgumentAddress or CilOperation.LoadLocalAddress or
            CilOperation.LoadFieldAddress or CilOperation.LoadStaticFieldAddress or CilOperation.Unbox or
            CilOperation.LoadArrayElementAddress or CilOperation.LoadRectangularArrayElementAddress
                ? CliTypeIdentity.ManagedByReference(type) : type;
    }

    private MethodInstanceModel ResolveMethod(EntityKey key)
    {
        var definition = _methods.GetMethod(key);
        return new(definition, CliTypeIdentity.FromDefinition(_types.GetTypeDefinition(definition.DeclaringType)),
            [], definition.Signature);
    }

    private int ProducedCount(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual =>
            (instruction.Operand is CilOperand.MethodInstance method
                ? method.Value.Signature.ReturnType
                : _methods.GetMethod(((CilOperand.Entity)instruction.Operand).Key).Signature.ReturnType) == CliValueKind.Void ? 0 : 1,
        CilOperation.CallIndirect => ((CilOperand.CallSite)instruction.Operand).Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        CilOperation.Nop or CilOperation.Pop or CilOperation.Branch or
            CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.Switch or CilOperation.StoreArgument or CilOperation.StoreLocal or
            CilOperation.StoreField or CilOperation.StoreStaticField or CilOperation.StoreObject or
            CilOperation.CopyObject or CilOperation.InitializeObject or CilOperation.CopyBlock or
            CilOperation.InitializeBlock or CilOperation.InitializeArrayData or
            CilOperation.StoreArrayElementReference or CilOperation.StoreArrayElement or
            CilOperation.StoreRectangularArrayElement or CilOperation.Constrained or CilOperation.Volatile or
            CilOperation.Readonly or CilOperation.Unaligned or CilOperation.Break or CilOperation.Throw or
            CilOperation.Rethrow or CilOperation.Leave or CilOperation.EndFinally or CilOperation.EndFilter or
            CilOperation.Return => 0,
        _ => 1,
    };

    private static CompilerException Invalid(ManagedMethodBody method, CilInstruction instruction, string reason) =>
        new(new CompilerDiagnostic(DiagnosticCode.InvalidCil, reason, method.Method.Definition.Name, instruction.Offset));

    private readonly record struct StorageIdentity(CliTypeIdentity? Type, bool Unresolved,
        bool BackingCovered, ImmutableHashSet<StackStorageOwner>? BackingOwners,
        ImmutableHashSet<CliTypeIdentity>? BackingCaptures = null, bool CompleteBackingKnown = false)
    {
        public ImmutableHashSet<StackStorageOwner> Owners => BackingOwners ?? ImmutableHashSet<StackStorageOwner>.Empty;

        public ImmutableHashSet<CliTypeIdentity> Captures => BackingCaptures ?? ImmutableHashSet<CliTypeIdentity>.Empty;
    }

    private sealed record StorageState(ImmutableArray<StorageIdentity> Stack, ImmutableArray<StorageIdentity> Locals);
}
