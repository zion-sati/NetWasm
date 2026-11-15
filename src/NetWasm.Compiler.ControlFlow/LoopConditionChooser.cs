// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

public sealed class LoopConditionChooser : ILoopConditionChooser
{
    public int? Choose(LoopConditionSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (IsBoundaryCondition(selection, selection.Header))
        {
            return selection.Header;
        }
        var candidates = selection.Component
            .Where(node => IsBoundaryCondition(selection, node))
            .Where(node => PartitionsLoop(selection, node))
            .Order()
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool IsBoundaryCondition(
        LoopConditionSelection selection,
        int node) =>
        selection.Graph.Successors[node].Length == 2 &&
        selection.Graph.Successors[node].Count(selection.Component.Contains) == 1;

    private static bool PartitionsLoop(
        LoopConditionSelection selection,
        int condition)
    {
        var continuationStart = selection.Graph.Successors[condition]
            .Single(selection.Component.Contains);
        var body = FindReachable(
            selection.Graph,
            selection.Header,
            condition,
            selection.Component);
        var continuation = FindReachable(
            selection.Graph,
            continuationStart,
            selection.Header,
            selection.Component);
        return !body.Overlaps(continuation);
    }

    private static ImmutableHashSet<int> FindReachable(
        ControlFlowGraph graph,
        int start,
        int stop,
        ImmutableHashSet<int> allowed)
    {
        var reachable = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Stack<int>();
        pending.Push(start);
        while (pending.TryPop(out var node))
        {
            if (node == stop || !allowed.Contains(node) || !reachable.Add(node))
            {
                continue;
            }
            foreach (var successor in graph.Successors[node])
            {
                pending.Push(successor);
            }
        }
        return reachable.ToImmutable();
    }

}
