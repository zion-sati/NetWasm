// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowDominanceAnalyzer : IControlFlowDominanceAnalyzer
{
    private readonly IDominatorTreeBuilder _trees;

    public ControlFlowDominanceAnalyzer(IDominatorTreeBuilder trees) =>
        _trees = trees ?? throw new ArgumentNullException(nameof(trees));

    // Dom(n) = {n} ∪ ∩ Dom(p) over n's predecessors within the blocks, its maximal fixed point:
    // the entry, each handler's and filter's first block (control enters it from wherever its
    // protected block throws, though a loop in the handler may branch back to it too) and a block
    // with no predecessor among the blocks dominate only themselves, and a block neither reaches is
    // dominated by every block.
    public DominatorTree Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        var handlerEntries = new HashSet<int>();
        foreach (var region in graph.MethodBody.ExceptionRegions)
        {
            handlerEntries.Add(graph.GetBlockAtOffset(region.HandlerOffset).Index);
            if (region.FilterOffset is int filter) handlerEntries.Add(graph.GetBlockAtOffset(filter).Index);
        }
        var roots = new List<int>();
        foreach (var block in blocks)
        {
            if (block == entry || handlerEntries.Contains(block) || !HasPredecessorWithin(graph, block, blocks)) roots.Add(block);
        }
        return _trees.Build(new DominanceProblem(
            blocks, roots, graph.Predecessors, graph.Successors));
    }

    private static bool HasPredecessorWithin(ControlFlowGraph graph, int block, ImmutableHashSet<int> blocks)
    {
        foreach (var predecessor in graph.Predecessors[block])
        {
            if (blocks.Contains(predecessor)) return true;
        }
        return false;
    }
}
