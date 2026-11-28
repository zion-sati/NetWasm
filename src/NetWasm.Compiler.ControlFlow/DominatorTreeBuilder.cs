// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

/// <summary>
/// A (post-)dominance problem over a set of blocks: its roots, which only a virtual root above them
/// dominates, and its edges in the analysis' direction (a block's dominators come from its inbound
/// blocks; the walk from the roots follows outbound ones). Edges leaving the set and edges into a
/// root are ignored.
/// </summary>
public sealed record DominanceProblem(
    ImmutableHashSet<int> Blocks,
    IReadOnlyCollection<int> Roots,
    BlockEdgeDictionary Inbound,
    BlockEdgeDictionary Outbound);

// Builds the dominator tree of a dominance problem.
public interface IDominatorTreeBuilder
{
    DominatorTree Build(DominanceProblem problem);
}

/// <summary>
/// Cooper, Harvey and Kennedy's iterative algorithm ("A Simple, Fast Dominance Algorithm") over
/// reverse postorder from a virtual root, then one walk of the tree for the intervals that answer
/// dominance queries. Every walk keeps an explicit stack, so a long chain needs no deep call stack.
/// Its arrays are as large as the problem's blocks (BlockNumbering), not the graph they belong to.
/// </summary>
public sealed class DominatorTreeBuilder : IDominatorTreeBuilder
{
    private const int Undefined = -2;

    public DominatorTree Build(DominanceProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var blocks = problem.Blocks;
        var numbering = BlockNumbering.For(blocks);
        var count = numbering.Count;
        var virtualRoot = count;
        var isRoot = new bool[count];
        foreach (var root in problem.Roots)
        {
            if (blocks.Contains(root)) isRoot[numbering.Number(root)] = true;
        }

        // Postorder from the virtual root, whose children are the roots in ascending order.
        var postorder = new int[count + 1];
        Array.Fill(postorder, -1);
        var reversePostorder = new List<int>(blocks.Count);
        var roots = new List<int>(problem.Roots);
        roots.Sort();
        var stack = new Stack<(int Block, int Next)>();
        var number = 0;
        foreach (var root in roots)
        {
            if (!blocks.Contains(root) || postorder[numbering.Number(root)] != -1) continue;
            postorder[numbering.Number(root)] = int.MaxValue;
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (block, next) = stack.Pop();
                var outbound = problem.Outbound[block];
                var descended = false;
                while (next < outbound.Length)
                {
                    var successor = outbound[next++];
                    if (!blocks.Contains(successor)) continue;
                    var successorNumber = numbering.Number(successor);
                    if (isRoot[successorNumber] || postorder[successorNumber] != -1) continue;
                    postorder[successorNumber] = int.MaxValue;
                    stack.Push((block, next));
                    stack.Push((successor, 0));
                    descended = true;
                    break;
                }
                if (descended) continue;
                postorder[numbering.Number(block)] = number++;
                reversePostorder.Add(block);
            }
        }
        postorder[virtualRoot] = number;
        reversePostorder.Reverse();

        var immediate = new int[count + 1];
        Array.Fill(immediate, Undefined);
        immediate[virtualRoot] = virtualRoot;
        foreach (var root in roots)
        {
            if (blocks.Contains(root)) immediate[numbering.Number(root)] = virtualRoot;
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var block in reversePostorder)
            {
                var blockNumber = numbering.Number(block);
                if (isRoot[blockNumber]) continue;
                var candidate = Undefined;
                foreach (var predecessor in problem.Inbound[block])
                {
                    if (!blocks.Contains(predecessor)) continue;
                    var predecessorNumber = numbering.Number(predecessor);
                    if (immediate[predecessorNumber] == Undefined) continue;
                    candidate = candidate == Undefined
                        ? predecessorNumber
                        : Intersect(immediate, postorder, predecessorNumber, candidate);
                }
                if (immediate[blockNumber] != candidate)
                {
                    immediate[blockNumber] = candidate;
                    changed = true;
                }
            }
        }
        while (changed);

        var parents = new int[count];
        Array.Fill(parents, DominatorTree.Unreached);
        foreach (var block in reversePostorder)
        {
            var blockNumber = numbering.Number(block);
            var parent = immediate[blockNumber];
            parents[blockNumber] = parent == virtualRoot ? DominatorTree.Root : parent;
        }
        return DominatorTree.FromParents(blocks, numbering, parents);
    }

    private static int Intersect(int[] immediate, int[] postorder, int left, int right)
    {
        while (left != right)
        {
            while (postorder[left] < postorder[right]) left = immediate[left];
            while (postorder[right] < postorder[left]) right = immediate[right];
        }
        return left;
    }
}
