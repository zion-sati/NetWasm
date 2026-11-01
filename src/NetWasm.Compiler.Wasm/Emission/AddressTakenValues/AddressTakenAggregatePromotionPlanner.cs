using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class AddressTakenAggregatePromotionPlanner(
    IFieldRepository fieldRepository,
    ITypeDefinitionResolver typeDefinitions,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fields) : IAddressTakenAggregatePromotionPlanner
{
    private readonly IFieldRepository _fieldRepository = fieldRepository ??
        throw new ArgumentNullException(nameof(fieldRepository));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IInstanceFieldLayoutProvider _fields = fields ??
        throw new ArgumentNullException(nameof(fields));

    public AddressTakenAggregatePromotionPlan Plan(
        AddressTakenAggregatePromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.CallSites);
        ArgumentNullException.ThrowIfNull(request.ByRefEffects);

        var method = request.Method;
        if (!request.Enabled || !method.ExceptionGroups.IsEmpty)
        {
            return AddressTakenAggregatePromotionPlan.Empty;
        }

        var reachableOffsets = method.InstructionEntryStacks.Keys.ToImmutableHashSet();
        var candidates = method.Header.Instructions
            .Where(instruction =>
                reachableOffsets.Contains(instruction.Offset) &&
                instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(CilOperandReader.GetIndex)
            .Distinct()
            .Where(index => (uint)index < (uint)method.Header.LocalSignatureTypes.Length)
            .Select(index => (Index: index, Type: method.Header.LocalSignatureTypes[index]))
            .Select(candidate => (candidate.Index, Plan: CreateLocalPlan(candidate.Type)))
            .Where(candidate => candidate.Plan is not null)
            .ToDictionary(candidate => candidate.Index, candidate => candidate.Plan!);
        if (candidates.Count == 0)
        {
            return AddressTakenAggregatePromotionPlan.Empty;
        }

        var rejected = new HashSet<int>();
        var projections = new Dictionary<int, AddressTakenAggregateProjection>();
        var synchronizations = new Dictionary<int, MutableSynchronization>();
        foreach (var block in method.Blocks.Values.OrderBy(block => block.StartOffset))
        {
            var instructions = block.Instructions
                .Where(instruction => reachableOffsets.Contains(instruction.Offset))
                .ToImmutableArray();
            if (instructions.IsEmpty)
            {
                continue;
            }

            var origins = EmptyOrigins(
                method.InstructionEntryStacks[instructions[0].Offset].Length);
            for (var index = 0; index < instructions.Length; index++)
            {
                var instruction = instructions[index];
                var entryCount = method.InstructionEntryStacks[instruction.Offset].Length;
                if (origins.Count != entryCount)
                {
                    RejectAll(origins, rejected);
                    origins = EmptyOrigins(entryCount);
                }

                if (instruction.Operation == CilOperation.LoadLocalAddress)
                {
                    var local = CilOperandReader.GetIndex(instruction);
                    origins.Add(candidates.ContainsKey(local) ? [local] : []);
                    continue;
                }

                if (instruction.Operation == CilOperation.Duplicate)
                {
                    origins.Add(origins[^1]);
                    continue;
                }

                if (instruction.Operation == CilOperation.LoadField)
                {
                    ApplyFieldLoad(instruction, origins, candidates, projections, rejected);
                    continue;
                }

                if (instruction.Operation == CilOperation.StoreField)
                {
                    ApplyFieldStore(instruction, origins, candidates, projections, rejected);
                    continue;
                }

                if (instruction.Operation == CilOperation.Call)
                {
                    ApplyDirectCall(
                        request,
                        instruction,
                        origins,
                        rejected,
                        synchronizations);
                    continue;
                }

                ApplyUnknown(method, instructions, index, instruction, origins, rejected);
            }

            RejectAll(origins, rejected);
        }

        foreach (var local in rejected)
        {
            candidates.Remove(local);
        }
        if (candidates.Count == 0)
        {
            return AddressTakenAggregatePromotionPlan.Empty;
        }

        var retainedProjections = projections
            .Where(pair => candidates.ContainsKey(pair.Value.LocalIndex))
            .ToImmutableDictionary();
        var operations = method.Header.Instructions.ToDictionary(
            instruction => instruction.Offset,
            instruction => instruction.Operation);
        candidates = candidates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                StorageSlots = SelectStorageSlots(
                    pair.Value.Size,
                    retainedProjections.Where(projection =>
                        projection.Value.LocalIndex == pair.Key),
                    operations,
                    synchronizations.Values.Count(call =>
                        call.Locals.Contains(pair.Key)) +
                    synchronizations.Values.Count(call =>
                        call.Reloads.Contains(pair.Key))),
            });
        // This is an execution-amortization guard, not a size policy. A promoted
        // lane adds live target values and call-boundary synchronization. Require
        // two direct projections per lane before paying those runtime costs.
        var executionCandidates = candidates
            .Where(candidate => HasEnoughProjectionTraffic(
                candidate,
                retainedProjections.Values))
            .ToDictionary();
        if (executionCandidates.Count == 0)
        {
            return AddressTakenAggregatePromotionPlan.Empty;
        }

        retainedProjections = retainedProjections
            .Where(pair => executionCandidates.ContainsKey(pair.Value.LocalIndex))
            .ToImmutableDictionary();
        var calls = CreateCoherentSynchronizations(
            method,
            executionCandidates.Keys.ToImmutableHashSet(),
            retainedProjections,
            synchronizations);
        return new(
            executionCandidates.OrderBy(pair => pair.Key).ToImmutableDictionary(),
            retainedProjections,
            calls);
    }

    private static ImmutableDictionary<
        int,
        AddressTakenAggregateCallSynchronization> CreateCoherentSynchronizations(
        NetWasm.Compiler.ControlFlow.Structured.StructuredMethod method,
        ImmutableHashSet<int> promotedLocals,
        ImmutableDictionary<int, AddressTakenAggregateProjection> projections,
        IReadOnlyDictionary<int, MutableSynchronization> callEffects)
    {
        var relevantCalls = callEffects
            .Select(pair => new
            {
                pair.Key,
                Locals = pair.Value.Locals.Where(promotedLocals.Contains)
                    .ToImmutableHashSet(),
                Reloads = pair.Value.Reloads.Where(promotedLocals.Contains)
                    .ToImmutableHashSet(),
            })
            .Where(call => !call.Locals.IsEmpty)
            .ToDictionary(
                call => call.Key,
                call => (call.Locals, call.Reloads));
        if (relevantCalls.Count == 0)
        {
            return [];
        }

        var operations = method.Header.Instructions.ToDictionary(
            instruction => instruction.Offset,
            instruction => instruction.Operation);
        var stores = projections
            .Where(pair => operations[pair.Key] == CilOperation.StoreField)
            .ToDictionary(pair => pair.Key, pair => pair.Value.LocalIndex);
        var inputs = new Dictionary<StructuredBlockId, HashSet<int>>
        {
            [method.EntryBlock] = [],
        };
        var outputs = new Dictionary<StructuredBlockId, HashSet<int>>();
        var pending = new Queue<StructuredBlockId>();
        var queued = new HashSet<StructuredBlockId>();
        pending.Enqueue(method.EntryBlock);
        queued.Add(method.EntryBlock);
        var required = new Dictionary<int, MutableSynchronization>();
        while (pending.TryDequeue(out var blockId))
        {
            queued.Remove(blockId);
            var block = method.Blocks[blockId];
            var dirty = new HashSet<int>(inputs[blockId]);
            foreach (var instruction in block.Instructions)
            {
                if (stores.TryGetValue(instruction.Offset, out var storedLocal))
                {
                    dirty.Add(storedLocal);
                }
                if (!relevantCalls.TryGetValue(
                        instruction.Offset,
                        out var synchronization))
                {
                    continue;
                }

                var before = synchronization.Locals
                    .Where(dirty.Contains)
                    .ToArray();
                if (before.Length != 0 || synchronization.Reloads.Count != 0)
                {
                    if (!required.TryGetValue(
                            instruction.Offset,
                            out var selected))
                    {
                        selected = new MutableSynchronization();
                        required.Add(instruction.Offset, selected);
                    }
                    selected.Locals.UnionWith(before);
                    selected.Reloads.UnionWith(synchronization.Reloads);
                }
                dirty.ExceptWith(synchronization.Locals);
            }

            if (outputs.TryGetValue(blockId, out var previous) &&
                previous.SetEquals(dirty))
            {
                continue;
            }
            outputs[blockId] = dirty;
            foreach (var successor in Successors(block.Exit))
            {
                if (!method.Blocks.ContainsKey(successor))
                {
                    continue;
                }
                if (!inputs.TryGetValue(successor, out var input))
                {
                    input = [];
                    inputs.Add(successor, input);
                }
                var previousCount = input.Count;
                input.UnionWith(dirty);
                var changed = input.Count != previousCount;
                if ((changed || !outputs.ContainsKey(successor)) &&
                    queued.Add(successor))
                {
                    pending.Enqueue(successor);
                }
            }
        }

        return required.ToImmutableDictionary(
            pair => pair.Key,
            pair => new AddressTakenAggregateCallSynchronization(
                pair.Value.Locals.Order().ToImmutableArray(),
                pair.Value.Reloads.Order().ToImmutableArray()));
    }

    private static IEnumerable<StructuredBlockId> Successors(
        StructuredBlockExit exit) => exit switch
        {
            StructuredFallthroughExit { Target: { } target } => [target],
            StructuredBranchExit branch => [branch.Target],
            StructuredConditionalExit conditional =>
                [conditional.WhenTaken, conditional.WhenNotTaken],
            StructuredLeaveExit leave => [leave.Target],
            _ => [],
        };

    private static bool HasEnoughProjectionTraffic(
        KeyValuePair<int, AddressTakenAggregateLocalPlan> candidate,
        IEnumerable<AddressTakenAggregateProjection> projections) =>
        projections.Count(projection => projection.LocalIndex == candidate.Key) >=
        candidate.Value.StorageSlots.Length * 2;

    internal static ImmutableArray<AddressTakenAggregateStorageSlot>
        SelectStorageSlots(
            int size,
            IEnumerable<KeyValuePair<int, AddressTakenAggregateProjection>> projections,
            Dictionary<int, CilOperation> operations,
            int synchronizationTouches)
    {
        var materialized = projections.ToImmutableArray();
        return EnumerateStorageLayouts(size)
            .Where(layout => materialized.All(projection =>
                Supports(layout, projection.Value)))
            .Select(layout => new
            {
                Layout = layout,
                Score = materialized.Sum(projection =>
                    ProjectionCost(
                        layout,
                        projection.Value,
                        operations[projection.Key])) +
                    synchronizationTouches * layout.Length,
            })
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Layout.Length)
            .ThenBy(candidate => string.Join(
                ',',
                candidate.Layout.Select(slot => $"{slot.ByteOffset}:{slot.Size}")),
                StringComparer.Ordinal)
            .First()
            .Layout;
    }

    private static ImmutableArray<ImmutableArray<AddressTakenAggregateStorageSlot>>
        EnumerateStorageLayouts(int size)
    {
        var layouts = ImmutableArray.CreateBuilder<
            ImmutableArray<AddressTakenAggregateStorageSlot>>();
        Enumerate(0, []);
        return layouts.ToImmutable();

        void Enumerate(
            int offset,
            ImmutableArray<AddressTakenAggregateStorageSlot> slots)
        {
            if (offset == size)
            {
                layouts.Add(slots);
                return;
            }

            foreach (var storageSize in new[] { sizeof(long), sizeof(int) })
            {
                if (offset + storageSize > size)
                {
                    continue;
                }
                var slot = new AddressTakenAggregateStorageSlot(
                    offset,
                    storageSize,
                    storageSize == sizeof(long)
                        ? CliValueKind.I8
                        : CliValueKind.I4);
                Enumerate(offset + storageSize, slots.Add(slot));
            }
        }
    }

    private static bool Supports(
        ImmutableArray<AddressTakenAggregateStorageSlot> slots,
        AddressTakenAggregateProjection projection) =>
        slots.Any(slot =>
            slot.ByteOffset == projection.ByteOffset &&
            slot.Size == projection.Size &&
            slot.StackKind == projection.StackKind) ||
        projection.StackKind == CliValueKind.I8 &&
        slots.Any(slot =>
            slot.ByteOffset == projection.ByteOffset &&
            slot.StackKind == CliValueKind.I4) &&
        slots.Any(slot =>
            slot.ByteOffset == projection.ByteOffset + sizeof(int) &&
            slot.StackKind == CliValueKind.I4);

    private static int ProjectionCost(
        ImmutableArray<AddressTakenAggregateStorageSlot> slots,
        AddressTakenAggregateProjection projection,
        CilOperation operation)
    {
        var direct = slots.Any(slot =>
            slot.ByteOffset == projection.ByteOffset &&
            slot.Size == projection.Size &&
            slot.StackKind == projection.StackKind);
        return direct
            ? 1
            : operation == CilOperation.LoadField ? 6 : 7;
    }

    private AddressTakenAggregateLocalPlan? CreateLocalPlan(CliTypeIdentity type)
    {
        if (type.StackKind != CliValueKind.ValueType)
        {
            return null;
        }

        var layout = _values.GetValueLayout(type);
        if (!layout.ReferenceOffsets.IsEmpty ||
            !layout.ByReferenceOffsets.IsEmpty ||
            layout.Size is < 8 or > 16 ||
            layout.Size % sizeof(int) != 0)
        {
            return null;
        }

        return AddressTakenAggregateLocalPlan.CreateI4Storage(type, layout.Size);
    }

    private void ApplyFieldLoad(
        CilInstruction instruction,
        List<ImmutableHashSet<int>> origins,
        IReadOnlyDictionary<int, AddressTakenAggregateLocalPlan> candidates,
        Dictionary<int, AddressTakenAggregateProjection> projections,
        HashSet<int> rejected)
    {
        if (origins.Count == 0)
        {
            return;
        }

        var receivers = origins[^1];
        origins.RemoveAt(origins.Count - 1);
        if (receivers.Count == 0)
        {
            origins.Add([]);
            return;
        }

        if (receivers.Count != 1 ||
            !TryCreateProjection(
                instruction,
                receivers.Single(),
                candidates,
                out var projection))
        {
            rejected.UnionWith(receivers);
        }
        else
        {
            projections.Add(instruction.Offset, projection);
        }
        origins.Add([]);
    }

    private void ApplyFieldStore(
        CilInstruction instruction,
        List<ImmutableHashSet<int>> origins,
        IReadOnlyDictionary<int, AddressTakenAggregateLocalPlan> candidates,
        Dictionary<int, AddressTakenAggregateProjection> projections,
        HashSet<int> rejected)
    {
        if (origins.Count < 2)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }

        var receivers = origins[^2];
        rejected.UnionWith(origins[^1]);
        origins.RemoveRange(origins.Count - 2, 2);
        if (receivers.Count == 0)
        {
            return;
        }

        if (receivers.Count != 1 ||
            !TryCreateProjection(
                instruction,
                receivers.Single(),
                candidates,
                out var projection))
        {
            rejected.UnionWith(receivers);
            return;
        }

        projections.Add(instruction.Offset, projection);
    }

    private bool TryCreateProjection(
        CilInstruction instruction,
        int localIndex,
        IReadOnlyDictionary<int, AddressTakenAggregateLocalPlan> candidates,
        out AddressTakenAggregateProjection projection)
    {
        projection = null!;
        var local = candidates[localIndex];

        if (!TryResolveField(
                local.Type,
                instruction.Operand,
                out var layout,
                out var fieldType) ||
            fieldType.StackKind is not (CliValueKind.I4 or CliValueKind.I8) ||
            layout.Size != (fieldType.StackKind == CliValueKind.I4
                ? sizeof(int)
                : sizeof(long)) ||
            layout.Offset % sizeof(int) != 0 ||
            layout.Offset < 0 ||
            layout.Offset + layout.Size > local.Size)
        {
            return false;
        }

        projection = new(
            localIndex,
            layout.Offset,
            layout.Size,
            fieldType.StackKind);
        return true;
    }

    private bool TryResolveField(
        CliTypeIdentity aggregateType,
        CilOperand operand,
        out FieldLayout layout,
        out CliTypeIdentity fieldType)
    {
        switch (operand)
        {
            case CilOperand.FieldInstance instance
                when !instance.Value.Definition.IsStatic &&
                    instance.Value.DeclaringType.Equals(aggregateType):
                layout = _fields.GetFieldLayout(instance.Value);
                fieldType = instance.Value.FieldType;
                return true;
            case CilOperand.Entity entity
                when aggregateType.Shape != CliTypeShape.GenericInstantiation:
                var definition = _fieldRepository.GetField(entity.Key);
                if (definition.IsStatic ||
                    definition.DeclaringType !=
                        _typeDefinitions.ResolveTypeIdentity(aggregateType).Key)
                {
                    break;
                }
                layout = _fields.GetFieldLayout(entity.Key);
                fieldType = definition.SignatureType;
                return true;
        }

        layout = default;
        fieldType = null!;
        return false;
    }

    private static void ApplyDirectCall(
        AddressTakenAggregatePromotionRequest request,
        CilInstruction instruction,
        List<ImmutableHashSet<int>> origins,
        HashSet<int> rejected,
        Dictionary<int, MutableSynchronization> synchronizations)
    {
        var key = new ManagedCallSiteKey(request.Caller, instruction.Offset);
        if (!request.CallSites.TryGetValue(key, out var callSite) ||
            callSite.Operation != ManagedCallOperation.Direct)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }

        var target = callSite.Target;
        var consumedCount = target.Signature.ParameterTypes.Length +
            (target.Definition.IsStatic ? 0 : 1);
        if (consumedCount > origins.Count ||
            !request.ByRefEffects.Methods.TryGetValue(
                callSite.TargetIdentity.CanonicalName,
                out var effects))
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }

        var first = origins.Count - consumedCount;
        var synchronization = new MutableSynchronization();
        for (var stackIndex = first; stackIndex < origins.Count; stackIndex++)
        {
            if (origins[stackIndex].IsEmpty)
            {
                continue;
            }

            var argumentIndex = stackIndex - first;
            if (effects.EscapedArguments.Contains(argumentIndex) ||
                effects.ReturnedArgumentAliases.Contains(argumentIndex))
            {
                rejected.UnionWith(origins[stackIndex]);
                continue;
            }

            synchronization.Locals.UnionWith(origins[stackIndex]);
            if (effects.WrittenArguments.Contains(argumentIndex))
            {
                synchronization.Reloads.UnionWith(origins[stackIndex]);
            }
        }

        if (synchronization.Locals.Count != 0)
        {
            synchronizations[instruction.Offset] = synchronization;
        }
        origins.RemoveRange(first, consumedCount);
        if (target.Signature.ReturnType != CliValueKind.Void)
        {
            origins.Add([]);
        }
    }

    private static void ApplyUnknown(
        NetWasm.Compiler.ControlFlow.Structured.StructuredMethod method,
        ImmutableArray<CilInstruction> instructions,
        int index,
        CilInstruction instruction,
        List<ImmutableHashSet<int>> origins,
        HashSet<int> rejected)
    {
        if (!origins.Any(origin => !origin.IsEmpty))
        {
            if (index + 1 < instructions.Length)
            {
                origins.Clear();
                origins.AddRange(EmptyOrigins(method.InstructionEntryStacks[
                    instructions[index + 1].Offset].Length));
            }
            return;
        }

        if (index + 1 >= instructions.Length)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }

        var outgoingCount = method.InstructionEntryStacks[
            instructions[index + 1].Offset].Length;
        var producedCount = ProducedCount(instruction);
        var consumedCount = origins.Count + producedCount - outgoingCount;
        if (consumedCount < 0 || consumedCount > origins.Count)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            origins.AddRange(EmptyOrigins(outgoingCount));
            return;
        }

        for (var stackIndex = origins.Count - consumedCount;
             stackIndex < origins.Count;
             stackIndex++)
        {
            rejected.UnionWith(origins[stackIndex]);
        }
        origins.RemoveRange(origins.Count - consumedCount, consumedCount);
        for (var produced = 0; produced < producedCount; produced++)
        {
            origins.Add([]);
        }
    }

    private static int ProducedCount(CilInstruction instruction) =>
        instruction.Operation switch
        {
            CilOperation.CallIndirect =>
                ((CilOperand.CallSite)instruction.Operand).Signature.ReturnType ==
                CliValueKind.Void ? 0 : 1,
            CilOperation.Nop or CilOperation.Pop or CilOperation.Branch or
                CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
                CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or
                CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or
                CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or
                CilOperation.BranchIfLessThanOrEqualUnsigned or
                CilOperation.StoreArgument or
                CilOperation.StoreLocal or CilOperation.StoreField or
                CilOperation.StoreStaticField or CilOperation.StoreObject or
                CilOperation.CopyObject or CilOperation.InitializeObject or
                CilOperation.CopyBlock or CilOperation.InitializeBlock or
                CilOperation.InitializeArrayData or
                CilOperation.StoreArrayElementReference or
                CilOperation.StoreArrayElement or
                CilOperation.StoreRectangularArrayElement or
                CilOperation.Constrained or CilOperation.Volatile or
                CilOperation.Readonly or CilOperation.Unaligned or
                CilOperation.Break or CilOperation.Throw or
                CilOperation.Rethrow or CilOperation.Leave or
                CilOperation.EndFinally or CilOperation.EndFilter or
                CilOperation.Return => 0,
            _ => 1,
        };

    private static List<ImmutableHashSet<int>> EmptyOrigins(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => ImmutableHashSet<int>.Empty)
            .ToList();

    private static void RejectAll(
        IEnumerable<ImmutableHashSet<int>> origins,
        HashSet<int> rejected)
    {
        foreach (var origin in origins)
        {
            rejected.UnionWith(origin);
        }
    }

    private sealed class MutableSynchronization
    {
        public HashSet<int> Locals { get; } = [];
        public HashSet<int> Reloads { get; } = [];
    }
}
