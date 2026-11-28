// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

/// <summary>
/// The (post-)dominators of a set of blocks as a tree: each block's immediate dominator, and an
/// interval per block from one walk of the tree, so whether one block dominates another is two
/// comparisons. Memory is linear in the blocks, where a set per block was quadratic, and its
/// arrays are indexed by BlockNumbering, so a tree of a few blocks of a large graph is small.
/// A block the analysis cannot reach from its roots is dominated by every block of the set, the
/// maximal fixed point of the data-flow equations the tree answers for.
/// </summary>
public sealed class DominatorTree : IEntryLookup<int, DominatorSet>, IMembership<int>
{
    internal const int Root = -1;
    internal const int Unreached = -2;

    private readonly ImmutableHashSet<int> _blocks;
    private readonly BlockNumbering _numbering;
    private readonly int[] _immediate;
    private readonly int[] _enter;
    private readonly int[] _exit;

    // Each array is indexed by a block's number; _immediate holds numbers, Root or Unreached.
    private DominatorTree(ImmutableHashSet<int> blocks, BlockNumbering numbering, int[] immediate, int[] enter, int[] exit)
    {
        _blocks = blocks;
        _numbering = numbering;
        _immediate = immediate;
        _enter = enter;
        _exit = exit;
    }

    /// <summary>
    /// A tree from each block's immediate dominator, null for a root; for a caller that already
    /// has them. Every block's chain must end at a root within the blocks.
    /// </summary>
    public static DominatorTree FromImmediateDominators(
        ImmutableHashSet<int> blocks,
        IReadOnlyDictionary<int, int?> immediateDominators)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(immediateDominators);
        var numbering = BlockNumbering.For(blocks);
        var parents = new int[numbering.Count];
        Array.Fill(parents, Unreached);
        foreach (var block in blocks)
        {
            if (!immediateDominators.TryGetValue(block, out var parent))
                throw new ArgumentException($"block {block} has no immediate dominator entry", nameof(immediateDominators));
            if (parent is int value && !blocks.Contains(value))
                throw new ArgumentException($"block {block}'s immediate dominator {value} is not a block", nameof(immediateDominators));
            parents[numbering.Number(block)] = parent is int dominator ? numbering.Number(dominator) : Root;
        }
        var tree = FromParents(blocks, numbering, parents);
        foreach (var block in blocks)
        {
            if (tree._exit[numbering.Number(block)] == 0)
                throw new ArgumentException($"block {block}'s dominators form a cycle", nameof(immediateDominators));
        }
        return tree;
    }

    // Numbers the tree in one walk from a virtual root above its roots: a block's interval holds
    // the intervals of the blocks it dominates. A block whose parent is Unreached gets none. The
    // parents are by block number.
    internal static DominatorTree FromParents(ImmutableHashSet<int> blocks, BlockNumbering numbering, int[] parents)
    {
        var virtualRoot = numbering.Count;
        var children = new List<int>?[virtualRoot + 1];
        foreach (var block in blocks)
        {
            var number = numbering.Number(block);
            var parent = parents[number];
            if (parent == Unreached) continue;
            (children[parent == Root ? virtualRoot : parent] ??= []).Add(number);
        }
        var enter = new int[virtualRoot + 1];
        var exit = new int[virtualRoot + 1];
        var clock = 1;
        var walk = new Stack<(int Block, int Next)>();
        enter[virtualRoot] = clock++;
        walk.Push((virtualRoot, 0));
        while (walk.Count > 0)
        {
            var (block, next) = walk.Pop();
            var below = children[block];
            if (below is not null && next < below.Count)
            {
                walk.Push((block, next + 1));
                var child = below[next];
                enter[child] = clock++;
                walk.Push((child, 0));
                continue;
            }
            exit[block] = clock++;
        }
        return new DominatorTree(blocks, numbering, parents, enter, exit);
    }

    /// <summary>The blocks the tree covers.</summary>
    public ImmutableHashSet<int> Blocks => _blocks;

    /// <summary>A covered block's dominators, itself included.</summary>
    public DominatorSet this[int block] =>
        Contains(block) ? new DominatorSet(this, block) : throw new KeyNotFoundException($"block {block} is not covered");

    /// <summary>Whether the tree covers the block.</summary>
    public bool Contains(int block) => _blocks.Contains(block);

    /// <summary>A block's dominators, if the tree covers it.</summary>
    public bool TryGetValue(int block, out DominatorSet dominators)
    {
        dominators = Contains(block) ? new DominatorSet(this, block) : default;
        return Contains(block);
    }

    /// <summary>
    /// A block's immediate dominator: null for a root of the analysis and for a block it cannot
    /// reach from its roots.
    /// </summary>
    public int? ImmediateDominator(int block)
    {
        var immediate = _immediate[_numbering.Number(this[block].Block)];
        return immediate >= 0 ? _numbering.Block(immediate) : null;
    }

    /// <summary>Whether <paramref name="dominator"/> dominates <paramref name="block"/>.</summary>
    public bool Dominates(int dominator, int block) => this[block].Contains(dominator);

    internal bool IsReached(int block) => _immediate[_numbering.Number(block)] != Unreached;

    // A block's immediate dominator, or Root or Unreached.
    internal int Parent(int block)
    {
        var parent = _immediate[_numbering.Number(block)];
        return parent >= 0 ? _numbering.Block(parent) : parent;
    }

    internal bool IsAncestorOrSelf(int ancestor, int block)
    {
        var ancestorNumber = _numbering.Number(ancestor);
        var blockNumber = _numbering.Number(block);
        return _enter[ancestorNumber] <= _enter[blockNumber] && _exit[blockNumber] <= _exit[ancestorNumber];
    }
}

/// <summary>
/// The dominators of one block: its chain of immediate dominators up the tree, or every covered
/// block for a block the analysis cannot reach.
/// </summary>
public readonly struct DominatorSet : IEnumerable<int>
{
    private readonly DominatorTree? _tree;

    internal DominatorSet(DominatorTree tree, int block)
    {
        _tree = tree;
        Block = block;
    }

    internal int Block { get; }

    /// <summary>Whether <paramref name="dominator"/> dominates this set's block.</summary>
    public bool Contains(int dominator)
    {
        var tree = _tree ?? throw new InvalidOperationException("the set belongs to no tree");
        if (!tree.Contains(dominator)) return false;
        if (!tree.IsReached(Block)) return true;
        return tree.IsReached(dominator) && tree.IsAncestorOrSelf(dominator, Block);
    }

    public IEnumerator<int> GetEnumerator()
    {
        var tree = _tree ?? throw new InvalidOperationException("the set belongs to no tree");
        if (!tree.IsReached(Block))
        {
            foreach (var block in tree.Blocks) yield return block;
            yield break;
        }
        for (var block = Block; block >= 0; block = tree.Parent(block)) yield return block;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
