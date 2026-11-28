// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowPostDominanceAnalyzer : IControlFlowPostDominanceAnalyzer
{
    private readonly IDominatorTreeBuilder _trees;

    public ControlFlowPostDominanceAnalyzer(IDominatorTreeBuilder trees) =>
        _trees = trees ?? throw new ArgumentNullException(nameof(trees));

    // PDom(n) = {n} ∪ ∩ PDom(s) over n's successors, its maximal fixed point: the stop block, a
    // block with a successor outside the allowed blocks and a block with no allowed successor
    // post-dominate only themselves, and a block that reaches none of them is post-dominated by
    // every allowed block. That is dominance on the reversed edges, with those blocks as roots.
    public DominatorTree Analyze(
        ControlFlowGraph graph,
        int? stopBlock,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(allowed);
        var roots = new List<int>();
        foreach (var block in allowed)
        {
            if (block == stopBlock || Ends(graph, block, allowed)) roots.Add(block);
        }
        return _trees.Build(new DominanceProblem(
            allowed, roots, graph.Successors, graph.Predecessors));
    }

    // Whether the block leaves the allowed blocks or has nowhere allowed to go.
    private static bool Ends(ControlFlowGraph graph, int block, ImmutableHashSet<int> allowed)
    {
        var anyAllowed = false;
        foreach (var successor in graph.Successors[block])
        {
            if (!allowed.Contains(successor)) return true;
            anyAllowed = true;
        }
        return !anyAllowed;
    }
}
