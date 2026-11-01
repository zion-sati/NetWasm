using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class ManagedByRefAggregatePromotionPlanner(
    IFieldRepository fieldRepository,
    ITypeDefinitionResolver typeDefinitions,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fields,
    IArgumentSignatureTypeResolver argumentTypes,
    ICilTypeOperandResolver typeOperands,
    IRuntimeIntrinsicRegistry intrinsics) : IManagedByRefAggregatePromotionPlanner
{
    private readonly IFieldRepository _fieldRepository = fieldRepository ??
        throw new ArgumentNullException(nameof(fieldRepository));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly IInstanceFieldLayoutProvider _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IArgumentSignatureTypeResolver _argumentTypes = argumentTypes ??
        throw new ArgumentNullException(nameof(argumentTypes));
    private readonly ICilTypeOperandResolver _typeOperands = typeOperands ??
        throw new ArgumentNullException(nameof(typeOperands));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public ManagedByRefAggregatePromotionPlan Plan(
        ManagedByRefAggregatePromotionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.CallSites);

        var method = request.Method;
        if (!request.Enabled || !method.ExceptionGroups.IsEmpty)
        {
            return ManagedByRefAggregatePromotionPlan.Empty;
        }

        var candidates = CandidateParameters(method.Header);
        if (candidates.Count != 1)
        {
            return ManagedByRefAggregatePromotionPlan.Empty;
        }

        var reachableOffsets = method.InstructionEntryStacks.Keys.ToImmutableHashSet();
        var rejected = new HashSet<int>();
        var projections = new Dictionary<int, ManagedByRefAggregateProjection>();
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

                switch (instruction.Operation)
                {
                    case CilOperation.LoadArgument:
                        {
                            var parameter = CilOperandReader.GetIndex(instruction);
                            origins.Add(candidates.ContainsKey(parameter)
                                ? [new(parameter, null)]
                                : []);
                            continue;
                        }
                    case CilOperation.StoreArgument:
                        if (candidates.ContainsKey(CilOperandReader.GetIndex(instruction)))
                        {
                            rejected.Add(CilOperandReader.GetIndex(instruction));
                        }
                        break;
                    case CilOperation.LoadArgumentAddress:
                        if (candidates.ContainsKey(CilOperandReader.GetIndex(instruction)))
                        {
                            rejected.Add(CilOperandReader.GetIndex(instruction));
                        }
                        break;
                    case CilOperation.Duplicate:
                        origins.Add(origins[^1]);
                        continue;
                    case CilOperation.LoadField:
                        ApplyFieldLoad(instruction, origins, candidates, projections, rejected);
                        continue;
                    case CilOperation.StoreField:
                        ApplyFieldStore(instruction, origins, candidates, projections, rejected);
                        continue;
                    case CilOperation.LoadFieldAddress:
                        ApplyFieldAddress(instruction, origins, candidates, rejected);
                        continue;
                    case CilOperation.LoadObject:
                        ApplyObjectLoad(
                            method.Header,
                            instruction,
                            origins,
                            projections,
                            rejected);
                        continue;
                    case CilOperation.StoreObject:
                        ApplyObjectStore(
                            method.Header,
                            instruction,
                            origins,
                            projections,
                            rejected);
                        continue;
                    case CilOperation.Call:
                        if (!IsAllowedPureCall(request, instruction))
                        {
                            rejected.UnionWith(candidates.Keys);
                        }
                        break;
                    case CilOperation.CallVirtual:
                    case CilOperation.CallIndirect:
                    case CilOperation.NewObject:
                        rejected.UnionWith(candidates.Keys);
                        break;
                }

                ApplyUnknown(method, instructions, index, instruction, origins, rejected);
            }

            RejectAll(origins, rejected);
        }

        foreach (var parameter in rejected)
        {
            candidates.Remove(parameter);
        }
        if (candidates.Count == 0)
        {
            return ManagedByRefAggregatePromotionPlan.Empty;
        }

        var retained = projections
            .Where(pair => candidates.ContainsKey(pair.Value.ParameterIndex))
            .ToImmutableDictionary();
        var operations = method.Header.Instructions.ToDictionary(
            instruction => instruction.Offset,
            instruction => instruction.Operation);
        candidates = candidates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                StorageSlots = AddressTakenAggregatePromotionPlanner.SelectStorageSlots(
                    pair.Value.Size,
                    retained
                        .Where(projection =>
                            projection.Value.ParameterIndex == pair.Key)
                        .Select(projection => new KeyValuePair<
                            int,
                            AddressTakenAggregateProjection>(
                            projection.Key,
                            new(
                                pair.Key,
                                projection.Value.ByteOffset,
                                projection.Value.Size,
                                projection.Value.StackKind))),
                    operations,
                    synchronizationTouches: 0),
            });
        candidates = candidates
            .Where(candidate => retained.Values.Count(projection =>
                    projection.ParameterIndex == candidate.Key) >=
                candidate.Value.StorageSlots.Length * 2)
            .ToDictionary();
        if (candidates.Count == 0)
        {
            return ManagedByRefAggregatePromotionPlan.Empty;
        }

        retained = retained
            .Where(pair => candidates.ContainsKey(pair.Value.ParameterIndex))
            .ToImmutableDictionary();
        return new(
            candidates.OrderBy(pair => pair.Key).ToImmutableDictionary(),
            retained);
    }

    private Dictionary<int, ManagedByRefAggregateParameterPlan> CandidateParameters(
        StructuredMethodHeader header)
    {
        var signature = header.MethodInstance?.Signature ?? header.Method.Signature;
        var parameterCount = signature.ParameterSignatureTypes.Length +
            (header.Method.IsStatic ? 0 : 1);
        var candidates = new Dictionary<int, ManagedByRefAggregateParameterPlan>();
        for (var index = 0; index < parameterCount; index++)
        {
            var parameter = _argumentTypes.Resolve(header, index);
            if (parameter.StackKind != CliValueKind.ManagedAddress ||
                parameter.ElementType is not { StackKind: CliValueKind.ValueType } aggregate)
            {
                continue;
            }

            var layout = _values.GetValueLayout(aggregate);
            if (!layout.ReferenceOffsets.IsEmpty ||
                !layout.ByReferenceOffsets.IsEmpty ||
                layout.Size is < 8 or > 16 ||
                layout.Size % sizeof(int) != 0 ||
                layout.Alignment % sizeof(int) != 0)
            {
                continue;
            }

            candidates.Add(
                index,
                new(
                    aggregate,
                    layout.Size,
                    AddressTakenAggregateLocalPlan.CreateI4Storage(
                        aggregate,
                        layout.Size).StorageSlots));
        }
        return candidates;
    }

    private void ApplyFieldLoad(
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        IReadOnlyDictionary<int, ManagedByRefAggregateParameterPlan> candidates,
        Dictionary<int, ManagedByRefAggregateProjection> projections,
        HashSet<int> rejected)
    {
        if (!TryConsumeRootProjection(
                instruction,
                origins,
                candidates,
                rejected,
                out var projection))
        {
            origins.Add([]);
            return;
        }
        projections.Add(instruction.Offset, projection);
        origins.Add([]);
    }

    private void ApplyFieldStore(
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        IReadOnlyDictionary<int, ManagedByRefAggregateParameterPlan> candidates,
        Dictionary<int, ManagedByRefAggregateProjection> projections,
        HashSet<int> rejected)
    {
        if (origins.Count < 2)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }
        rejected.UnionWith(origins[^1].Select(origin => origin.ParameterIndex));
        origins.RemoveAt(origins.Count - 1);
        if (TryConsumeRootProjection(
                instruction,
                origins,
                candidates,
                rejected,
                out var projection))
        {
            projections.Add(instruction.Offset, projection);
        }
    }

    private void ApplyFieldAddress(
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        IReadOnlyDictionary<int, ManagedByRefAggregateParameterPlan> candidates,
        HashSet<int> rejected)
    {
        if (!TryConsumeRootProjection(
                instruction,
                origins,
                candidates,
                rejected,
                out var projection))
        {
            origins.Add([]);
            return;
        }
        origins.Add([new(projection.ParameterIndex, projection)]);
    }

    private void ApplyObjectLoad(
        StructuredMethodHeader header,
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        Dictionary<int, ManagedByRefAggregateProjection> projections,
        HashSet<int> rejected)
    {
        if (!TryConsumeFieldAddress(
                header,
                instruction,
                origins,
                rejected,
                out var projection))
        {
            origins.Add([]);
            return;
        }
        projections.Add(instruction.Offset, projection);
        origins.Add([]);
    }

    private void ApplyObjectStore(
        StructuredMethodHeader header,
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        Dictionary<int, ManagedByRefAggregateProjection> projections,
        HashSet<int> rejected)
    {
        if (origins.Count < 2)
        {
            RejectAll(origins, rejected);
            origins.Clear();
            return;
        }
        rejected.UnionWith(origins[^1].Select(origin => origin.ParameterIndex));
        origins.RemoveAt(origins.Count - 1);
        if (TryConsumeFieldAddress(
                header,
                instruction,
                origins,
                rejected,
                out var projection))
        {
            projections.Add(instruction.Offset, projection);
        }
    }

    private bool TryConsumeRootProjection(
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        IReadOnlyDictionary<int, ManagedByRefAggregateParameterPlan> candidates,
        HashSet<int> rejected,
        out ManagedByRefAggregateProjection projection)
    {
        projection = null!;
        if (origins.Count == 0)
        {
            return false;
        }
        var receivers = origins[^1];
        origins.RemoveAt(origins.Count - 1);
        if (receivers.Count == 0)
        {
            return false;
        }
        if (receivers.Count != 1 || receivers.Single().Projection is not null)
        {
            rejected.UnionWith(receivers.Select(origin => origin.ParameterIndex));
            return false;
        }

        var origin = receivers.Single();
        if (!candidates.TryGetValue(origin.ParameterIndex, out var candidate) ||
            !TryCreateProjection(
                candidate,
                origin.ParameterIndex,
                instruction.Operand,
                addressAlreadyProjected: false,
                out projection))
        {
            rejected.Add(origin.ParameterIndex);
            return false;
        }
        return true;
    }

    private bool TryConsumeFieldAddress(
        StructuredMethodHeader header,
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        HashSet<int> rejected,
        out ManagedByRefAggregateProjection projection)
    {
        projection = null!;
        if (origins.Count == 0)
        {
            return false;
        }
        var addresses = origins[^1];
        origins.RemoveAt(origins.Count - 1);
        if (addresses.Count == 0)
        {
            return false;
        }
        if (addresses.Count != 1 || addresses.Single().Projection is not { } field)
        {
            rejected.UnionWith(addresses.Select(origin => origin.ParameterIndex));
            return false;
        }

        var loadedType = _typeOperands.Resolve(instruction, header.MethodInstance);
        if (loadedType.StackKind != field.StackKind ||
            _values.GetValueLayout(loadedType).Size != field.Size)
        {
            rejected.Add(field.ParameterIndex);
            return false;
        }
        projection = field with { AddressAlreadyProjected = true };
        return true;
    }

    private bool TryCreateProjection(
        ManagedByRefAggregateParameterPlan candidate,
        int parameterIndex,
        CilOperand operand,
        bool addressAlreadyProjected,
        out ManagedByRefAggregateProjection projection)
    {
        projection = null!;
        if (!TryResolveField(candidate.Type, operand, out var layout, out var fieldType) ||
            fieldType.StackKind is not (CliValueKind.I4 or CliValueKind.I8) ||
            layout.Size != (fieldType.StackKind == CliValueKind.I4
                ? sizeof(int)
                : sizeof(long)) ||
            layout.Offset % sizeof(int) != 0 ||
            layout.Offset < 0 ||
            layout.Offset + layout.Size > candidate.Size)
        {
            return false;
        }
        projection = new(
            parameterIndex,
            layout.Offset,
            layout.Size,
            fieldType.StackKind,
            addressAlreadyProjected);
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
                if (!definition.IsStatic &&
                    definition.DeclaringType ==
                        _typeDefinitions.ResolveTypeIdentity(aggregateType).Key)
                {
                    layout = _fields.GetFieldLayout(entity.Key);
                    fieldType = definition.SignatureType;
                    return true;
                }
                break;
        }
        layout = default;
        fieldType = null!;
        return false;
    }

    private bool IsAllowedPureCall(
        ManagedByRefAggregatePromotionRequest request,
        CilInstruction instruction)
    {
        var key = new ManagedCallSiteKey(request.Caller, instruction.Offset);
        return request.CallSites.TryGetValue(key, out var callSite) &&
            callSite.Operation == ManagedCallOperation.Direct &&
            _intrinsics.TryGetIntrinsic(
                callSite.Target.Definition.Key,
                out var intrinsic) &&
            intrinsic == RuntimeIntrinsic.IntegerDivRem;
    }

    private static void ApplyUnknown(
        StructuredMethod method,
        ImmutableArray<CilInstruction> instructions,
        int index,
        CilInstruction instruction,
        List<ImmutableHashSet<Origin>> origins,
        HashSet<int> rejected)
    {
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
            rejected.UnionWith(origins[stackIndex]
                .Select(origin => origin.ParameterIndex));
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

    private static List<ImmutableHashSet<Origin>> EmptyOrigins(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => ImmutableHashSet<Origin>.Empty)
            .ToList();

    private static void RejectAll(
        IEnumerable<ImmutableHashSet<Origin>> origins,
        HashSet<int> rejected)
    {
        foreach (var origin in origins.SelectMany(origin => origin))
        {
            rejected.Add(origin.ParameterIndex);
        }
    }

    private sealed record Origin(
        int ParameterIndex,
        ManagedByRefAggregateProjection? Projection);
}
