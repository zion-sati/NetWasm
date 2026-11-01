using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The words of a method's value frame that hold references, one bit a
// reference-sized word, and the locals and arguments whose frame storage they
// cover. A collection traces those words as they are when it runs, so it sees
// what a callee stores through a span or a ref field into them, and a safepoint
// need not publish them.
internal sealed record ValueFrameReferenceMap(
    ImmutableArray<ulong> Words,
    ImmutableHashSet<(bool IsArgument, int Index)> Storages)
{
    public static ValueFrameReferenceMap Empty { get; } = new([], []);

    // A local's or an argument's own storage, as against what a byref it holds
    // addresses.
    public bool Covers(RootSource source) => source.Kind switch
    {
        RootSourceKind.Local => Storages.Contains((false, source.Index)),
        RootSourceKind.Argument => Storages.Contains((true, source.Index)),
        _ => false,
    };

    // The root map without the sources the frame's map covers, its slots
    // allocated again for what remains.
    public MethodRootMap Exclude(MethodRootMap map, IRootSlotAllocator slots)
    {
        if (Storages.IsEmpty) return map;
        var safepoints = map.Safepoints.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Roots = [.. pair.Value.Roots.Where(source => !Covers(source))],
                ConstructorCallRoots = [.. pair.Value.ConstructorCallRoots.Where(source => !Covers(source))],
            });
        return map with
        {
            Slots = slots.Allocate([.. safepoints.Values.OrderBy(safepoint => safepoint.IlOffset)]),
            Safepoints = safepoints,
        };
    }
}

internal interface IValueFrameReferenceMapPlanner
{
    ValueFrameReferenceMap Plan(
        StructuredMethodHeader header,
        ValueFrameLayout layout,
        FilterEnvironmentLayout filters,
        ManagedAggregateSsaPlan? aggregates);
}

// Maps the frame storage whose memory is the only copy of its value: a local
// promoted to a Wasm local, an aggregate split into lane locals or a slot a
// filter captures keeps its own publication, as does a reference the target
// does not align to its reference size.
internal sealed class ValueFrameReferenceMapPlanner(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IArgumentSignatureTypeResolver argumentTypes) : IValueFrameReferenceMapPlanner
{
    public ValueFrameReferenceMap Plan(
        StructuredMethodHeader header,
        ValueFrameLayout layout,
        FilterEnvironmentLayout filters,
        ManagedAggregateSsaPlan? aggregates)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(filters);
        var size = layouts.Target.ObjectReferenceSize;
        var bits = new SortedSet<int>();
        var storages = ImmutableHashSet.CreateBuilder<(bool IsArgument, int Index)>();
        foreach (var (index, offset) in layout.LocalOffsets)
        {
            if (!layout.AddressTakenScalarPromotions.PromotedLocals.ContainsKey(index))
                Add(false, index, offset, header.LocalSignatureTypes[index]);
        }
        foreach (var (index, offset) in layout.ArgumentOffsets)
            Add(true, index, offset, argumentTypes.Resolve(header, index));
        if (bits.Count == 0) return ValueFrameReferenceMap.Empty;
        var words = new ulong[bits.Max / 64 + 1];
        foreach (var bit in bits) words[bit / 64] |= 1UL << (bit % 64);
        return new([.. words], storages.ToImmutable());

        void Add(bool isArgument, int index, int offset, CliTypeIdentity type)
        {
            if (filters.Captures.ContainsKey(new(isArgument, index)) || aggregates?.Storages.ContainsKey(new(
                    isArgument ? ManagedAggregateStorageKind.Argument : ManagedAggregateStorageKind.Local, index)) == true)
                return;
            var lanes = Lanes(type);
            if (lanes.IsEmpty || lanes.Any(lane => (offset + lane) % size != 0)) return;
            foreach (var lane in lanes) bits.Add((offset + lane) / size);
            storages.Add((isArgument, index));
        }
    }

    private ImmutableArray<int> Lanes(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified) type = type.ElementType!;
        return type.StackKind switch
        {
            CliValueKind.ManagedReference or CliValueKind.ManagedAddress => [0],
            CliValueKind.ValueType => values.GetValueLayout(type).ReferenceOffsets,
            _ => [],
        };
    }
}
