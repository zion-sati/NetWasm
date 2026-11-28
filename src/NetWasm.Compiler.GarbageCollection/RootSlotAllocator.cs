// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Each root source, in (Kind, Index, ByteOffset) order, takes the lowest slot that no source before it holds at a
// safepoint where both are roots: a greedy coloring of the graph in which each safepoint's roots are all adjacent. The
// graph's edges are never listed (a safepoint with R roots has R squared of them, and a Debug method keeps most of its
// locals live at every safepoint): identical safepoints count once, and each keeps the slots its colored roots hold and
// the lowest slot none of them holds. Every slot below the highest of those lowest slots among a source's safepoints is
// held at one of them, so the source takes that slot unless one of its safepoints holds it; only then does it read the
// slots its safepoints hold.
// Cost: linear in the roots of the distinct safepoints, except that a source whose starting slot is held reads the slots
// held at its safepoints once (what the pairwise graph cost for every source).
public sealed class RootSlotAllocator : IRootSlotAllocator
{
    public ImmutableDictionary<RootSource, int> Allocate(
        IReadOnlyList<SafepointRootMap> safepoints)
    {
        ArgumentNullException.ThrowIfNull(safepoints);

        var cliques = new HashSet<ImmutableArray<RootSource>>(RootsComparer.Instance);
        var memberships = new Dictionary<RootSource, List<int>>();
        foreach (var safepoint in safepoints)
        {
            Add(safepoint.Roots);
            Add(safepoint.ConstructorCallRoots);
        }

        var held = new HashSet<int>?[cliques.Count];
        var lowest = new int[cliques.Count];
        var slots = ImmutableDictionary.CreateBuilder<RootSource, int>();
        foreach (var source in memberships.Keys
                     .OrderBy(source => source.Kind)
                     .ThenBy(source => source.Index)
                     .ThenBy(source => source.ByteOffset))
        {
            var memberOf = memberships[source];
            var slot = 0;
            foreach (var clique in memberOf) slot = Math.Max(slot, lowest[clique]);
            if (memberOf.Exists(clique => held[clique]?.Contains(slot) == true))
            {
                var unavailable = new HashSet<int>();
                foreach (var clique in memberOf)
                {
                    if (held[clique] is { } taken) unavailable.UnionWith(taken);
                }
                while (unavailable.Contains(slot)) slot++;
            }
            slots.Add(source, slot);
            foreach (var clique in memberOf)
            {
                var taken = held[clique] ??= [];
                taken.Add(slot);
                while (taken.Contains(lowest[clique])) lowest[clique]++;
            }
        }
        return slots.ToImmutable();

        void Add(ImmutableArray<RootSource> roots)
        {
            if (roots.Length == 0 || !cliques.Add(roots)) return;
            var clique = cliques.Count - 1;
            foreach (var source in roots)
            {
                if (!memberships.TryGetValue(source, out var memberOf)) memberships[source] = memberOf = [];
                if (memberOf.Count == 0 || memberOf[^1] != clique) memberOf.Add(clique);
            }
        }
    }

    // Two safepoints' roots as the same set when they list the same sources in the same order (the analyzer orders them).
    private sealed class RootsComparer : IEqualityComparer<ImmutableArray<RootSource>>
    {
        public static RootsComparer Instance { get; } = new();

        public bool Equals(ImmutableArray<RootSource> x, ImmutableArray<RootSource> y) => x.AsSpan().SequenceEqual(y.AsSpan());

        public int GetHashCode(ImmutableArray<RootSource> obj)
        {
            var hash = new HashCode();
            foreach (var source in obj) hash.Add(source);
            return hash.ToHashCode();
        }
    }
}
