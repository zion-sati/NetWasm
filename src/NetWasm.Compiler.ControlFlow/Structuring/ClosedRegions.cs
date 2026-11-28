// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// The regions an else-if chain's arm or a switch's target may be: a closed region, which the plain steps structure on
// its own and which ends at a join or at the loop's break or continue.
internal static class ClosedRegions
{
    // Blocks a region may transfer to without its structure ending at the join: the innermost loop's continue and
    // break targets, which the region's sequence ends with.
    internal static ImmutableHashSet<int> Terminals(ControlFlowStructuringState state) =>
        [.. state.LoopContinueTargets.Take(1), .. state.LoopExitTargets.Take(1)];

    // Blocks no region takes: those the exception-region and dispatcher steps structure (protected regions' entries,
    // dispatchers' blocks, loop headers), the active dispatcher's blocks, which the sequence builder continues through
    // the dispatcher, and the loop's break and continue targets. Asked block by block of the state as it is, so a step
    // pays for the blocks it visits; a copy of them all at every step made structuring grow with the method squared.
    // The steps ask before they structure anything, while the state is as it was when they began.
    internal static ReservedBlocks Reserved(ControlFlowStructuringState state, ImmutableHashSet<int> terminals) =>
        new(state, terminals);

    // Whether the region starting at the block is closed: entered only through its start from the entries, every
    // block in the allowed blocks and not reserved, and every edge out of it going to the join or to a terminal. Its
    // blocks are its own, so it overlaps no other region. Returns whether it reaches the join, or null when it is not
    // closed. Starting at the join or at a terminal, it is that transfer alone.
    internal static bool? Reaches(
        ControlFlowStructuringState state,
        int start,
        IReadOnlySet<int> entries,
        int? join,
        ImmutableHashSet<int> allowed,
        ImmutableHashSet<int> terminals,
        ReservedBlocks reserved) =>
        Reaches(state, start, entries, join, allowed, terminals, reserved, []);

    // As above, adding the region's blocks to `region`.
    internal static bool? Reaches(
        ControlFlowStructuringState state,
        int start,
        IReadOnlySet<int> entries,
        int? join,
        ImmutableHashSet<int> allowed,
        ImmutableHashSet<int> terminals,
        ReservedBlocks reserved,
        HashSet<int> blocks)
    {
        if (start == join)
        {
            return true;
        }
        if (terminals.Contains(start))
        {
            return false;
        }
        var region = new HashSet<int> { start };
        var pending = new Stack<int>();
        pending.Push(start);
        var reachesJoin = false;
        while (pending.TryPop(out var node))
        {
            if (!allowed.Contains(node) || reserved.Contains(node))
            {
                return null;
            }
            foreach (var successor in state.Graph.Successors[node])
            {
                if (successor == join)
                {
                    reachesJoin = true;
                }
                else if (!terminals.Contains(successor) && region.Add(successor))
                {
                    pending.Push(successor);
                }
            }
        }
        // Every block's predecessors are the entries or blocks of the region.
        foreach (var node in region)
        {
            foreach (var predecessor in state.Graph.Predecessors[node])
            {
                if (!entries.Contains(predecessor) && !region.Contains(predecessor))
                {
                    return null;
                }
            }
        }
        blocks.UnionWith(region);
        return reachesJoin;
    }
}

// The blocks ClosedRegions.Reserved names, asked one at a time.
internal readonly struct ReservedBlocks(ControlFlowStructuringState state, ImmutableHashSet<int> terminals)
{
    public bool Contains(int block) =>
        state.ExceptionGroupsByEntry.ContainsKey(block) ||
        state.DispatchersByNode.ContainsKey(block) ||
        state.LoopsByHeader.ContainsKey(block) && !state.SuppressedLoopHeaders.Contains(block) ||
        state.ActiveDispatchers.TryPeek(out var active) && active.Contains(block) ||
        terminals.Contains(block);
}
