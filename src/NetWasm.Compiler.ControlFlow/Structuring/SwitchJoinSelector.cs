// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Where a switch's cases meet. Outer is the block most cases' code reaches; Join the block most of those reach first,
// before Outer: in a loop, the code after the switch, which a break reaches and a continue skips for the increment.
// Each case goes to Join when its code reaches Join, otherwise to Outer when it reaches Outer, and otherwise meets
// no other case (it ends in a return, a throw, or the loop's break or continue). There is no meeting when no two cases
// meet.
internal sealed record SwitchJoins(SwitchMeeting? Meeting, ImmutableDictionary<int, int?> Destinations);

// The block most cases' code reaches, and the one most of those reach first; the same block when there is one.
internal sealed record SwitchMeeting(int Outer, int Join);

internal interface ISwitchJoinSelector
{
    // The targets' joins from their post-dominators, which stop at the terminals (the loop's break and continue).
    SwitchJoins Select(DominatorTree postDominators, IReadOnlyCollection<int> targets, ImmutableHashSet<int> terminals);
}

internal sealed class SwitchJoinSelector : ISwitchJoinSelector
{
    // Linear in the blocks the targets' post-dominators reach: each block is visited once, however many targets share
    // the path above it (a switch ahead of thousands of blocks, a table of thousands of targets).
    public SwitchJoins Select(DominatorTree postDominators, IReadOnlyCollection<int> targets, ImmutableHashSet<int> terminals)
    {
        ArgumentNullException.ThrowIfNull(postDominators);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(terminals);
        // The forest of the targets' post-dominators: each block's parent is its immediate post-dominator, none past a
        // terminal (the loop's break or continue) or for a block outside the tree (the enclosing join).
        var distinct = targets.Distinct().ToArray();
        var parents = new Dictionary<int, int?>();
        foreach (var target in distinct)
        {
            for (int? block = target; block is int current && !parents.ContainsKey(current);)
            {
                var parent = postDominators.Contains(current) ? postDominators.ImmediateDominator(current) : null;
                parents[current] = parent is int up && terminals.Contains(up) ? null : parent;
                block = parents[current];
            }
        }
        var children = parents.Keys.ToDictionary(block => block, _ => new List<int>());
        foreach (var (block, parent) in parents)
        {
            if (parent is int up) children[up].Add(block);
        }
        // How many targets each block's code is reached from: the targets below it, itself included.
        var reached = parents.Keys.ToDictionary(block => block, _ => 0);
        foreach (var target in distinct) reached[target]++;
        var pending = children.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        var leaves = new Queue<int>(pending.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        while (leaves.TryDequeue(out var block))
        {
            if (parents[block] is not int up) continue;
            reached[up] += reached[block];
            if (--pending[up] == 0) leaves.Enqueue(up);
        }
        // Each block's interval in a walk of the forest, for whether one block is above another.
        var (enter, exit) = (new Dictionary<int, int>(), new Dictionary<int, int>());
        var clock = 0;
        foreach (var root in parents.Where(pair => pair.Value is null).Select(pair => pair.Key).Order())
        {
            var walk = new Stack<(int Block, int Next)>([(root, 0)]);
            enter[root] = clock++;
            while (walk.TryPop(out var frame))
            {
                if (frame.Next < children[frame.Block].Count)
                {
                    walk.Push((frame.Block, frame.Next + 1));
                    var child = children[frame.Block][frame.Next];
                    enter[child] = clock++;
                    walk.Push((child, 0));
                    continue;
                }
                exit[frame.Block] = clock++;
            }
        }
        bool Above(int ancestor, int block) => enter[ancestor] <= enter[block] && exit[block] <= exit[ancestor];

        var meeting = reached.Where(pair => pair.Value >= 2).Select(pair => pair.Key).ToArray();
        if (meeting.Length == 0)
        {
            return new SwitchJoins(null, distinct.ToImmutableDictionary(target => target, _ => (int?)null));
        }
        var outer = Most(meeting);
        var inside = meeting.Where(block => block != outer && Above(outer, block)).ToArray();
        var join = inside.Length == 0 ? outer : Most(inside);
        return new SwitchJoins(new SwitchMeeting(outer, join), distinct.ToImmutableDictionary(target => target, target =>
            Above(join, target) ? join : Above(outer, target) ? outer : (int?)null));

        // The block the most targets reach; of those, one no other of them lies below (the nearest the targets); then
        // the lowest index. The count never falls going up the forest, so a nearest block has no child as reached.
        int Most(IReadOnlyCollection<int> blocks)
        {
            var most = blocks.Max(block => reached[block]);
            return blocks
                .Where(block => reached[block] == most && !children[block].Any(child => reached[child] == most))
                .Min();
        }
    }
}
