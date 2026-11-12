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
// what a callee stores through a byref, a span or a ref field into them, and a
// safepoint need not publish them.
internal sealed record ValueFrameReferenceMap(
    ImmutableArray<ulong> Words,
    ImmutableHashSet<(bool IsArgument, int Index)> Storages)
{
    public static ValueFrameReferenceMap Empty { get; } = new([], []);

    // The byte ranges of mapped words that nothing writes before the first
    // safepoint, so the frame's entry zeroes them.
    public ImmutableArray<(int Offset, int Size)> EntryClears { get; init; } = [];

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

// Maps every frame storage a byref can address, since a callee publishes only
// the byref, not what it addresses: locals, arguments, filter captures and the
// temporaries newobj constructs value types in. An aggregate split into lane
// locals and a slot a filter captures keep their own publication as well,
// since their current value can be elsewhere; their frame words hold their own
// type or zero, so a stale word keeps an object alive no longer than the frame,
// as an untracked local does on CoreCLR. A local promoted to a Wasm local holds
// no references.
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
        var cleared = new SortedSet<int>();
        var storages = ImmutableHashSet.CreateBuilder<(bool IsArgument, int Index)>();
        foreach (var (index, offset) in layout.LocalOffsets)
        {
            if (!layout.AddressTakenScalarPromotions.PromotedLocals.ContainsKey(index))
                AddStorage(false, index, offset, header.LocalSignatureTypes[index]);
        }
        foreach (var (index, offset) in layout.ArgumentOffsets)
            AddStorage(true, index, offset, argumentTypes.Resolve(header, index));
        // A captured slot is its local's or argument's storage from the frame's
        // entry, which zeroes or copies it.
        foreach (var capture in filters.Captures.Values) Add(capture.Offset, capture.Type, clear: false);
        // newobj zeroes its temporary only when it runs, after earlier safepoints.
        foreach (var (instruction, type) in layout.ConstructedValueTypes)
            Add(layout.TemporaryOffsets[instruction], type, clear: true);
        if (bits.Count == 0) return ValueFrameReferenceMap.Empty;
        var words = new ulong[bits.Max / 64 + 1];
        foreach (var bit in bits) words[bit / 64] |= 1UL << (bit % 64);
        return new([.. words], storages.ToImmutable()) { EntryClears = Runs(cleared, size) };

        void AddStorage(bool isArgument, int index, int offset, CliTypeIdentity type)
        {
            var aggregate = aggregates?.Storages.ContainsKey(new(
                isArgument ? ManagedAggregateStorageKind.Argument : ManagedAggregateStorageKind.Local, index)) == true;
            // An aggregate argument split into lanes is read from the caller's
            // copy; its own storage is written first at a materialization.
            if (!Add(offset, type, clear: aggregate && isArgument) || aggregate ||
                filters.Captures.ContainsKey(new(isArgument, index)))
                return;
            storages.Add((isArgument, index));
        }

        bool Add(int offset, CliTypeIdentity type, bool clear)
        {
            var lanes = Lanes(type);
            foreach (var lane in lanes)
            {
                if ((offset + lane) % size != 0)
                {
                    throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                        $"A reference in the value frame at offset {offset + lane} is not aligned to {size} bytes.",
                        header.Method.Name));
                }
                bits.Add((offset + lane) / size);
                if (clear) cleared.Add((offset + lane) / size);
            }
            return !lanes.IsEmpty;
        }
    }

    // Consecutive words as byte ranges.
    private static ImmutableArray<(int Offset, int Size)> Runs(SortedSet<int> words, int size)
    {
        var runs = ImmutableArray.CreateBuilder<(int Offset, int Size)>();
        foreach (var word in words)
        {
            if (runs.Count != 0 && runs[^1].Offset + runs[^1].Size == word * size)
                runs[^1] = (runs[^1].Offset, runs[^1].Size + size);
            else
                runs.Add((word * size, size));
        }
        return runs.ToImmutable();
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
