// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowDistanceFinder : IControlFlowDistanceFinder
{
    public Dictionary<int, int> Find(ControlFlowStructuringState state,
        int start,
        int? stop,
        ImmutableHashSet<int> allowed)
    {
        if (!state.Distances.TryGetValue(allowed, out var byEndpoints))
        {
            byEndpoints = [];
            state.Distances.Add(allowed, byEndpoints);
        }
        var key = (start, stop ?? -1);
        if (byEndpoints.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var distances = new Dictionary<int, int>();
        var pending = new Queue<(int Node, int Distance)>();
        pending.Enqueue((start, 0));
        while (pending.TryDequeue(out var item))
        {
            if (!allowed.Contains(item.Node) || !distances.TryAdd(item.Node, item.Distance))
            {
                continue;
            }
            if (item.Node == stop)
            {
                continue;
            }
            foreach (var successor in state.ContinuationGraph.Successors[item.Node])
            {
                pending.Enqueue((successor, item.Distance + 1));
            }
        }
        byEndpoints.Add(key, distances);
        return distances;
    }
}
