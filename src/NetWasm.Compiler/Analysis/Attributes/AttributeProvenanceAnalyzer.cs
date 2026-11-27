// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: a finite exact-value analysis. Merging different proofs loses the
// proof; it never turns a runtime choice into a set of retained types.
internal sealed class AttributeProvenanceAnalyzer(
    ICalledMethodResolver calls,
    ITypeOperandResolver types,
    IReadonlyTypeFieldValueResolver fields,
    ImmutableHashSet<EntityKey> nullableUnwrapMethods,
    CliTypeIdentity nullableDefinition,
    IAttributeAssemblySources? assemblies = null,
    IAttributeTypeValueClassifier? typeValues = null,
    IFieldRepository? fieldTypes = null) : IAttributeProvenanceAnalyzer
{
    public ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var addressedLocals = body.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToImmutableHashSet();
        var unwindWrites = body.ExceptionRegions
            .Where(region => region.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault or CilExceptionRegionKind.Filter)
            .SelectMany(region => WrittenLocals(
                region.Kind == CilExceptionRegionKind.Filter ? region.FilterOffset!.Value : region.HandlerOffset,
                region.Kind == CilExceptionRegionKind.Filter ? region.HandlerOffset : region.HandlerOffset + region.HandlerLength))
            .ToImmutableHashSet();
        var entries = new Dictionary<int, ProofState>();
        var pending = new Queue<int>();
        var results = ImmutableDictionary.CreateBuilder<int, ImmutableArray<AttributeValueProof>>();
        var values = new BlockLocalValues<AttributeValueProof>(graph.Blocks, body.Locals.Length, _ => true);
        // The locals' proofs are a LocalValueTrie the blocks share: an array of them all at every block grew with the
        // blocks times the locals that cross them.
        var runtimeTypes = typeValues is not null;
        Func<AttributeValueProof, AttributeValueProof, AttributeValueProof> meet =
            (existing, incoming) => Joined(existing, incoming, runtimeTypes);
        Merge(graph.Entry.Index, new([], LocalValueTrie<AttributeValueProof>.Empty(values.Crossing)));

        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var entry = entries[blockIndex];
            var stack = entry.Stack.ToList();
            var locals = entry.Locals;
            bool? condition = null;
            bool? filterAccepted = null;
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var instruction = block.Instructions[index];
                results[instruction.Offset] = [.. stack];
                // An exception can leave before the current instruction's writes.
                // Use the handler's validated stack and preserve local proofs only
                // when every incoming exceptional edge agrees.
                if (CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                {
                    foreach (var successor in graph.ExceptionalSuccessors[blockIndex])
                    {
                        var exceptionalLocals = locals;
                        foreach (var local in unwindWrites)
                        {
                            exceptionalLocals = values.Forget(exceptionalLocals, local);
                        }
                        Merge(successor, new(
                            new AttributeValueProof[controlFlow.EntryStacks[successor].Length], exceptionalLocals));
                    }
                }
                if (instruction.Operation == CilOperation.EndFilter)
                {
                    filterAccepted = stack[^1].Condition;
                }
                if (instruction.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse)
                {
                    condition = stack[^1].Condition;
                    if (instruction.Operation == CilOperation.BranchIfFalse && condition is { } value)
                    {
                        condition = !value;
                    }
                }
                var outgoingCount = index + 1 < block.Instructions.Length
                    ? controlFlow.InstructionEntryStacks[block.Instructions[index + 1].Offset].Length
                    : graph.Successors[blockIndex].IsEmpty
                        ? 0
                        : controlFlow.EntryStacks[graph.Successors[blockIndex][0]].Length;
                Transfer(instruction, outgoingCount, stack, ref locals);
            }
            values.EndBlock();

            if (block.Terminator.Operation == CilOperation.EndFilter && filterAccepted != false)
            {
                foreach (var region in body.ExceptionRegions.Where(region =>
                    region.Kind == CilExceptionRegionKind.Filter &&
                    block.Terminator.Offset >= region.FilterOffset && block.Terminator.Offset < region.HandlerOffset))
                {
                    var handler = graph.GetBlockAtOffset(region.HandlerOffset).Index;
                    Merge(handler, new(new AttributeValueProof[controlFlow.EntryStacks[handler].Length], locals));
                }
            }
            foreach (var successor in graph.Successors[blockIndex])
            {
                if (condition is { } taken && block.Terminator.Operand is CilOperand.BranchTarget target &&
                    taken != (graph.GetBlock(successor).StartOffset == target.Offset) &&
                    target.Offset != block.Terminator.NextOffset)
                {
                    continue;
                }
                Merge(successor, new([.. stack], locals));
            }
        }
        return results.ToImmutable();

        IEnumerable<int> WrittenLocals(int start, int end) => body.Instructions
            .Where(instruction => instruction.Offset >= start && instruction.Offset < end &&
                instruction.Operation == CilOperation.StoreLocal)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value);

        void Merge(int block, ProofState incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, new((AttributeValueProof[])incoming.Stack.Clone(), incoming.Locals));
                pending.Enqueue(block);
                return;
            }
            var stackChanged = Join(existing.Stack, incoming.Stack, runtimeTypes);
            var locals = existing.Locals.Meet(incoming.Locals, meet);
            if (!ReferenceEquals(locals, existing.Locals))
            {
                entries[block] = existing with { Locals = locals };
            }
            if (stackChanged || !ReferenceEquals(locals, existing.Locals))
            {
                pending.Enqueue(block);
            }
        }

        void Transfer(CilInstruction instruction, int outgoingCount,
            List<AttributeValueProof> stack, ref LocalValueTrie<AttributeValueProof> locals)
        {
            var operation = instruction.Operation;
            switch (operation)
            {
                case CilOperation.LoadTypeToken:
                    stack.Add(new(AttributeValueKind.TypeToken, types.Resolve(instruction, body.MethodInstance)));
                    return;
                case CilOperation.MaterializeType:
                    stack[^1] = stack[^1].Kind == AttributeValueKind.TypeToken
                        ? stack[^1] with { Kind = AttributeValueKind.Type }
                        : typeValues is null ? default : new(AttributeValueKind.RuntimeType);
                    return;
                case CilOperation.GetObjectType:
                    stack[^1] = typeValues is null ? default : new(AttributeValueKind.RuntimeType);
                    return;
                case CilOperation.LoadArgument:
                    stack.Add(Typed(ArgumentType(((CilOperand.Index)instruction.Operand).Value)));
                    return;
                case CilOperation.LoadArgumentAddress:
                    stack.Add(AddressOf(ArgumentType(((CilOperand.Index)instruction.Operand).Value)));
                    return;
                case CilOperation.LoadLocalAddress:
                    stack.Add(AddressOf(body.LocalSignatureTypes.ElementAtOrDefault(((CilOperand.Index)instruction.Operand).Value)));
                    return;
                case CilOperation.LoadStaticFieldAddress when instruction.Operand is CilOperand.FieldInstance staticFieldAddress:
                    stack.Add(AddressOf(staticFieldAddress.Value.FieldType));
                    return;
                case CilOperation.LoadStaticFieldAddress when instruction.Operand is CilOperand.Entity staticFieldAddressEntity:
                    stack.Add(AddressOf(FieldType(staticFieldAddressEntity.Key)));
                    return;
                case CilOperation.LoadFieldAddress when instruction.Operand is CilOperand.FieldInstance fieldAddress:
                    stack[^1] = AddressOf(fieldAddress.Value.FieldType);
                    return;
                case CilOperation.LoadFieldAddress when instruction.Operand is CilOperand.Entity fieldAddressEntity:
                    stack[^1] = AddressOf(FieldType(fieldAddressEntity.Key));
                    return;
                case CilOperation.LoadArrayElementReference:
                    var elements = stack[^2].Kind;
                    stack.RemoveRange(stack.Count - 2, 2);
                    stack.Add(elements == AttributeValueKind.RuntimeTypeArray ? new(AttributeValueKind.RuntimeType) : default);
                    return;
                case CilOperation.LoadArrayElement:
                    stack.RemoveRange(stack.Count - 2, 2);
                    stack.Add(typeValues is null ? default : Typed(types.Resolve(instruction, body.MethodInstance)));
                    return;
                case CilOperation.LoadArrayElementAddress:
                    stack.RemoveRange(stack.Count - 2, 2);
                    stack.Add(typeValues is null ? default : AddressOf(types.Resolve(instruction, body.MethodInstance)));
                    return;
                case CilOperation.LoadObject:
                    // ldobj names the loaded type; ldind.ref names only an object, so its address says what it holds.
                    var loadedType = typeValues is null ? default : Typed(types.Resolve(instruction, body.MethodInstance));
                    stack[^1] = loadedType.Kind == AttributeValueKind.Unknown && stack[^1].Kind == AttributeValueKind.RuntimeTypeAddress
                        ? new(AttributeValueKind.RuntimeType)
                        : loadedType;
                    return;
                case CilOperation.LoadStaticField when instruction.Operand is CilOperand.FieldInstance staticField:
                    stack.Add(Typed(staticField.Value.FieldType));
                    return;
                case CilOperation.LoadStaticField when instruction.Operand is CilOperand.Entity staticEntity:
                    stack.Add(Typed(FieldType(staticEntity.Key)));
                    return;
                case CilOperation.IsInstance:
                    stack[^1] = typeValues is null ? default : Typed(types.Resolve(instruction, body.MethodInstance));
                    return;
                case CilOperation.LoadNull:
                    stack.Add(new(AttributeValueKind.Null));
                    return;
                case CilOperation.LoadInt32:
                    stack.Add(new(AttributeValueKind.Int32, Integer: ((CilOperand.ConstantI4)instruction.Operand).Value));
                    return;
                case CilOperation.Duplicate:
                    stack.Add(stack[^1]);
                    return;
                case CilOperation.LoadLocal:
                    var loaded = ((CilOperand.Index)instruction.Operand).Value;
                    var held = addressedLocals.Contains(loaded) ? default : values.Read(locals, loaded);
                    stack.Add(held.Kind == AttributeValueKind.Unknown ? Typed(body.LocalSignatureTypes.ElementAtOrDefault(loaded)) : held);
                    return;
                case CilOperation.StoreLocal:
                    locals = values.Write(locals, ((CilOperand.Index)instruction.Operand).Value, stack[^1]);
                    stack.RemoveAt(stack.Count - 1);
                    return;
                case CilOperation.CastClass:
                    // A successful cast preserves identity; a failed cast does not
                    // reach the query. Keep the original cast in the executable CIL.
                    if (typeValues is not null && stack[^1].Kind == AttributeValueKind.Unknown)
                    {
                        stack[^1] = Typed(types.Resolve(instruction, body.MethodInstance));
                    }
                    return;
                case CilOperation.Leave:
                    var destination = ((CilOperand.BranchTarget)instruction.Operand).Offset;
                    var crossedFinally = body.ExceptionRegions.Where(region =>
                        region.Kind == CilExceptionRegionKind.Finally &&
                        instruction.Offset >= region.TryOffset && instruction.Offset < region.TryOffset + region.TryLength &&
                        (destination < region.TryOffset || destination >= region.TryOffset + region.TryLength)).ToArray();
                    foreach (var region in crossedFinally)
                    {
                        foreach (var local in WrittenLocals(region.HandlerOffset, region.HandlerOffset + region.HandlerLength))
                        {
                            locals = values.Forget(locals, local);
                        }
                    }
                    foreach (var region in crossedFinally)
                    {
                        var handler = graph.GetBlockAtOffset(region.HandlerOffset).Index;
                        Merge(handler, new(new AttributeValueProof[controlFlow.EntryStacks[handler].Length], locals));
                    }
                    return;
                case CilOperation.LoadField when instruction.Operand is CilOperand.FieldInstance field:
                    stack[^1] = fields.Resolve(field.Value) is { } type
                        ? new(AttributeValueKind.Type, type)
                        : Typed(field.Value.FieldType);
                    return;
                case CilOperation.LoadField when instruction.Operand is CilOperand.Entity field:
                    stack[^1] = fields.Resolve(field.Key) is { } fieldType
                        ? new(AttributeValueKind.Type, fieldType)
                        : Typed(FieldType(field.Key));
                    return;
                case CilOperation.Call:
                case CilOperation.CallVirtual:
                case CilOperation.NewObject:
                    var method = calls.Resolve(instruction)!;
                    var consumed = method.Signature.ParameterTypes.Length +
                        (operation != CilOperation.NewObject && !method.Definition.IsStatic ? 1 : 0);
                    var result = operation == CilOperation.Call &&
                        nullableUnwrapMethods.Contains(method.Definition.Key) &&
                        stack[^1] is { Kind: AttributeValueKind.Type, Type: { } argument }
                            ? Unwrap(argument)
                            : assemblies?.Prove(operation, method, stack) ?? default;
                    stack.RemoveRange(stack.Count - consumed, consumed);
                    if (operation == CilOperation.NewObject)
                    {
                        stack.Add(result);
                    }
                    else if (method.Signature.ReturnType != CliValueKind.Void)
                    {
                        stack.Add(result.Kind == AttributeValueKind.Unknown ? Typed(method.Signature.ReturnSignatureType) : result);
                    }
                    return;
                case CilOperation.CallIndirect:
                    var signature = ((CilOperand.CallSite)instruction.Operand).Signature;
                    var indirectConsumed = signature.ParameterTypes.Length + 1;
                    stack.RemoveRange(stack.Count - indirectConsumed, indirectConsumed);
                    if (signature.ReturnType != CliValueKind.Void)
                    {
                        stack.Add(default);
                    }
                    return;
            }

            // Other CIL instructions produce at most one value (dup and calls
            // are handled above). Validated stack heights identify the consumed
            // suffix without losing unaffected values beneath it.
            var produced = operation switch
            {
                CilOperation.Nop or CilOperation.Pop or CilOperation.Branch or
                CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
                CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned or
                CilOperation.StoreArgument or CilOperation.StoreField or
                CilOperation.StoreStaticField or CilOperation.StoreObject or CilOperation.CopyObject or
                CilOperation.InitializeObject or CilOperation.CopyBlock or CilOperation.InitializeBlock or
                CilOperation.InitializeArrayData or CilOperation.StoreArrayElementReference or
                CilOperation.StoreArrayElement or CilOperation.StoreRectangularArrayElement or
                CilOperation.Constrained or CilOperation.Volatile or CilOperation.Readonly or
                CilOperation.Unaligned or CilOperation.Break or CilOperation.Throw or CilOperation.Rethrow or
                CilOperation.Leave or CilOperation.EndFinally or CilOperation.EndFilter or CilOperation.Return => 0,
                _ => 1,
            };
            var preserved = Math.Max(0, outgoingCount - produced);
            stack.RemoveRange(preserved, stack.Count - preserved);
            while (stack.Count < outgoingCount)
            {
                stack.Add(default);
            }
        }

        // A value typed Type the analysis cannot name.
        // A field of a non-generic type, which its definition types.
        CliTypeIdentity? FieldType(EntityKey key) => fieldTypes?.GetField(key).SignatureType;

        // A Type, an array of Types or an address of a Type, by its static type.
        AttributeValueProof Typed(CliTypeIdentity? type)
        {
            if (typeValues is null || Unmodified(type) is not { } unmodified)
            {
                return default;
            }
            return unmodified.Shape switch
            {
                CliTypeShape.SzArray => IsTypeValue(unmodified.ElementType) ? new(AttributeValueKind.RuntimeTypeArray) : default,
                CliTypeShape.ManagedByReference => AddressOf(unmodified.ElementType),
                _ => typeValues.IsTypeValue(unmodified) ? new(AttributeValueKind.RuntimeType) : default,
            };
        }

        // The address of a location of the given static type.
        AttributeValueProof AddressOf(CliTypeIdentity? type) =>
            IsTypeValue(type) ? new(AttributeValueKind.RuntimeTypeAddress) : default;

        bool IsTypeValue(CliTypeIdentity? type) =>
            typeValues is not null && Unmodified(type) is { } unmodified && typeValues.IsTypeValue(unmodified);

        CliTypeIdentity? ArgumentType(int index)
        {
            if (body.MethodInstance is not { } method)
            {
                return null;
            }
            var parameter = method.Definition.IsStatic ? index : index - 1;
            return parameter < 0 ? method.DeclaringType : method.Signature.ParameterSignatureTypes.ElementAtOrDefault(parameter);
        }

        AttributeValueProof Unwrap(CliTypeIdentity type) =>
            type.Shape == CliTypeShape.GenericInstantiation && type.ElementType!.Equals(nullableDefinition)
                ? new(AttributeValueKind.Type, type.TypeArguments[0])
                : new(AttributeValueKind.Null);
    }

    private static bool Join(AttributeValueProof[] existing, AttributeValueProof[] incoming, bool runtimeTypes)
    {
        var changed = false;
        for (var index = 0; index < existing.Length; index++)
        {
            var joined = Joined(existing[index], incoming[index], runtimeTypes);
            changed |= existing[index] != joined;
            existing[index] = joined;
        }
        return changed;
    }

    // What two paths' proofs of one value meet to: the same proof, or nothing known (the default), except that types
    // (or nulls) of different kinds still meet as a Type the analysis cannot name.
    private static AttributeValueProof Joined(AttributeValueProof existing, AttributeValueProof incoming, bool runtimeTypes) =>
        existing.Kind == AttributeValueKind.Unknown || existing == incoming
            ? existing
            : runtimeTypes && TypeLike(existing) && TypeLike(incoming)
                ? new AttributeValueProof(AttributeValueKind.RuntimeType)
                : default;

    // A type without its custom modifiers (a ref readonly return's modreq).
    private static CliTypeIdentity? Unmodified(CliTypeIdentity? type)
    {
        while (type is { Shape: CliTypeShape.Modified })
        {
            type = type.ElementType;
        }
        return type;
    }

    private static bool TypeLike(AttributeValueProof proof) =>
        proof.Kind is AttributeValueKind.Type or AttributeValueKind.RuntimeType or AttributeValueKind.Null;

    private sealed record ProofState(AttributeValueProof[] Stack, LocalValueTrie<AttributeValueProof> Locals);
}
