using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateStorageFinder(
    IArgumentSignatureTypeResolver argumentTypes,
    IValueLayoutProvider values,
    ITargetLayout layouts) : IManagedAggregateStorageFinder
{
    private readonly IArgumentSignatureTypeResolver _argumentTypes = argumentTypes ??
        throw new ArgumentNullException(nameof(argumentTypes));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Find(
        ManagedAggregateValueAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage>();
        var methodEffects = request.Method.Header.MethodInstance is { } methodInstance &&
            request.ByRefEffects.TryGetValue(
                methodInstance.CanonicalName,
                out var resolvedEffects)
                ? resolvedEffects
                : null;
        foreach (var index in request.Method.Header.Instructions
                     .Where(instruction =>
                         instruction.Operation == CilOperation.LoadArgumentAddress)
                     .Select(CilOperandReader.GetIndex)
                     .Distinct())
        {
            if (methodEffects?.RequiresOwnedStorage(index) == true)
            {
                AddStorage(
                    result,
                    new(ManagedAggregateStorageKind.Argument, index),
                    _argumentTypes.Resolve(request.Method.Header, index),
                    owned: true,
                    request.References);
            }
        }

        foreach (var index in request.Method.Header.Instructions
                     .Where(instruction =>
                         instruction.Operation == CilOperation.LoadLocalAddress)
                     .Select(CilOperandReader.GetIndex)
                     .Distinct()
                     .Where(index => !request.ExcludedLocals.Contains(index)))
        {
            AddStorage(
                result,
                new(ManagedAggregateStorageKind.Local, index),
                request.Method.Header.LocalSignatureTypes[index],
                owned: true,
                request.References);
        }
        return result;
    }

    private void AddStorage(
        Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> storages,
        ManagedAggregateStorageId id,
        CliTypeIdentity type,
        bool owned,
        bool references)
    {
        if (type.StackKind != CliValueKind.ValueType)
        {
            return;
        }
        var layout = _values.GetValueLayout(type);
        if (layout.Size is < 8 or > 16 ||
            layout.Size % sizeof(int) != 0 ||
            LaneShapes(layout, references) is not { } lanes)
        {
            return;
        }
        storages.Add(id, new(id, type, layout.Size, owned) { LaneShapes = lanes });
    }

    // A reference or byref field becomes a lane of its own, which the root
    // planner publishes like a local of that kind; the rest is 4-byte lanes.
    private ImmutableArray<ManagedAggregateLaneShape>? LaneShapes(ValueLayout layout, bool references)
    {
        if (layout.ReferenceOffsets.IsEmpty && layout.ByReferenceOffsets.IsEmpty)
        {
            return [];
        }
        if (!references)
        {
            return null;
        }
        var pointer = _layouts.Target.ObjectReferenceSize;
        var tracked = layout.ReferenceOffsets.Concat(layout.ByReferenceOffsets).ToHashSet();
        var lanes = ImmutableArray.CreateBuilder<ManagedAggregateLaneShape>();
        for (var offset = 0; offset < layout.Size;)
        {
            if (!tracked.Contains(offset))
            {
                lanes.Add(new(offset, sizeof(int), CliValueKind.I4));
                offset += sizeof(int);
                continue;
            }
            if (offset % pointer != 0 || offset + pointer > layout.Size)
            {
                return null;
            }
            lanes.Add(new(offset, pointer, layout.ByReferenceOffsets.Contains(offset)
                ? CliValueKind.ManagedAddress
                : CliValueKind.ManagedReference));
            offset += pointer;
        }
        // A tracked offset inside another lane would be split.
        return tracked.All(offset => lanes.Any(lane => lane.ByteOffset == offset)) ? lanes.ToImmutable() : null;
    }
}
