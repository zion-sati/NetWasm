// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RootSlotAllocator : IRootSlotAllocator
{
    public ImmutableDictionary<RootSource, int> Allocate(
        IReadOnlyList<SafepointRootMap> safepoints)
    {
        ArgumentNullException.ThrowIfNull(safepoints);

        var interference = new Dictionary<RootSource, HashSet<RootSource>>();
        foreach (var safepoint in safepoints)
        {
            AddInterference(safepoint.Roots);
            AddInterference(safepoint.ConstructorCallRoots);
        }

        var slots = ImmutableDictionary.CreateBuilder<RootSource, int>();
        foreach (var source in interference.Keys
                     .OrderBy(source => source.Kind)
                     .ThenBy(source => source.Index)
                     .ThenBy(source => source.ByteOffset))
        {
            var unavailable = interference[source]
                .Where(slots.ContainsKey)
                .Select(neighbor => slots[neighbor])
                .ToHashSet();
            var slot = 0;
            while (unavailable.Contains(slot))
            {
                slot++;
            }
            slots.Add(source, slot);
        }
        return slots.ToImmutable();

        void AddInterference(ImmutableArray<RootSource> roots)
        {
            foreach (var source in roots)
            {
                interference.TryAdd(source, []);
            }
            for (var left = 0; left < roots.Length; left++)
            {
                for (var right = left + 1; right < roots.Length; right++)
                {
                    interference[roots[left]].Add(roots[right]);
                    interference[roots[right]].Add(roots[left]);
                }
            }
        }
    }
}
