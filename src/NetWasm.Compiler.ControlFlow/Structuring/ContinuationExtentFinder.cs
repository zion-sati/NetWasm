// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A protected block's continuations are structured over the blocks from their targets up to the boundary (the next
// protected block in the same scope, or the scope's end), which they reach last. That holds while every block they reach
// before the boundary branches only to blocks before it or to the boundary's own block. A block that branches past it (a
// goto over the next protected block to shared code after it, or a finally copy's jump to a shared suspension exit) would
// lose that branch and run on into the next protected block, so its continuations extend to the method's end, as
// continuations whose targets lie past the boundary do. A leave is not followed: it ends the sequence, and its target is
// reached by the protected blocks it leaves.
internal sealed class ContinuationExtentFinder(IReachableBlockFinder reachableBlocks) : IContinuationExtentFinder
{
    private readonly IReachableBlockFinder _reachableBlocks =
        reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));

    public int End(ControlFlowGraph graph, IReadOnlyCollection<int> targets, int boundary, int methodEnd)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || boundary >= methodEnd) return boundary;
        if (targets.Any(target => target > boundary)) return methodEnd;
        var before = targets.Where(target => target < boundary).ToArray();
        if (before.Length == 0) return boundary;
        var within = BlocksStarting.Between(graph, before.Min(), boundary);
        var reached = new HashSet<int>();
        foreach (var target in before)
        {
            var entry = graph.GetBlockAtOffset(target).Index;
            if (reached.Contains(entry)) continue;
            reached.UnionWith(_reachableBlocks.Find(graph, entry, within));
        }
        var runsPast = reached.Any(block => graph.GetBlock(block).Terminator.Operation != CilOperation.Leave &&
            graph.Successors[block].Any(successor => graph.GetBlock(successor).StartOffset > boundary));
        return runsPast ? methodEnd : boundary;
    }
}
