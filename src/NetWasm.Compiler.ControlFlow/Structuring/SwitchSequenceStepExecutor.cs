// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A CIL switch the decoder turned into a chain of compares (LoweredSwitchChain) dispatches through a table instead,
// when the policy takes its size: each distinct target is a labeled block, nested innermost first around the head's
// code and the table, and followed by its target's region; the join's labeled block encloses them all. A case is the
// branch to its target's label, so cases sharing a target share a label. Every target must be a closed region (the
// join, the loop's break or continue, or blocks only its cases enter), and the value an int32; otherwise the branching
// step structures the chain as it is.
internal sealed class SwitchSequenceStepExecutor(
    IControlFlowPostDominatorFinder postDominators,
    ISwitchChainReader chains,
    ISwitchJoinSelector joins,
    ISwitchTablePolicy tables) : IStructuredSequenceStepExecutor
{
    private readonly IControlFlowPostDominatorFinder _postDominators =
        postDominators ?? throw new ArgumentNullException(nameof(postDominators));
    private readonly ISwitchChainReader _chains = chains ?? throw new ArgumentNullException(nameof(chains));
    private readonly ISwitchJoinSelector _joins = joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly ISwitchTablePolicy _tables = tables ?? throw new ArgumentNullException(nameof(tables));

    public StructuredSequenceStepResult Execute(
        IStructuredControlFlowBuilder structuredControlFlowBuilder,
        ControlFlowStructuringState state,
        int currentBlockOffset,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path,
        ImmutableArray<StructuredRegionDraft>.Builder regions)
    {
        var notHandled = new StructuredSequenceStepResult(StructuredSequenceStepDisposition.NotHandled, currentBlockOffset);
        var terminals = ClosedRegions.Terminals(state);
        var reserved = ClosedRegions.Reserved(state, terminals);
        if (_chains.Read(state.Validated, state.Graph.GetBlock(currentBlockOffset)) is not { } chain ||
            !Owns(state, chain, stop, allowed, reserved))
        {
            return notHandled;
        }
        var targets = chain.Targets;
        if (!_tables.Tables(chain.Cases.Length, targets.Length))
        {
            return notHandled;
        }
        // A case may leave the blocks being structured only for the enclosing join or the loop's break or continue.
        var inner = targets.Where(target => !terminals.Contains(target)).ToArray();
        if (inner.Any(target => !allowed.Contains(target) && target != stop))
        {
            return notHandled;
        }
        // Where the cases' code meets: the join, and past the code after it the outer join, which a case reaching the
        // join first skips (a loop's continue, past the code after the switch). The enclosing join is a block of the
        // post-dominators' even when the sequence's blocks leave it out.
        var joins = _joins.Select(
            _postDominators.Find(state, stop, stop is int enclosing && !allowed.Contains(enclosing) ? allowed.Add(enclosing) : allowed),
            inner,
            terminals);
        // Cases meeting at the head would loop through the switch: no join (the chain's other blocks are entered only
        // from the chain, so no case's code meets there).
        var meeting = joins.Meeting;
        if (meeting is { } met && (met.Join == chain.Head.Index || met.Outer == chain.Head.Index))
        {
            return notHandled;
        }
        var (outer, join) = (meeting?.Outer, meeting?.Join);
        var entries = chain.Entries();
        // Each case's code ends at its join, or at the loop's break or continue, or in a return or throw.
        var destinations = targets.ToDictionary(target => target, target => joins.Destinations.GetValueOrDefault(target) ?? join);
        var reaches = new Dictionary<int, bool>();
        var regionBlocks = new HashSet<int>();
        foreach (var target in targets.Where(target => target != join && target != outer))
        {
            if (ClosedRegions.Reaches(state, target, entries[target], destinations[target], allowed, terminals, reserved,
                    destinations[target] == join ? regionBlocks : []) is not bool reachesDestination)
            {
                return notHandled;
            }
            reaches[target] = reachesDestination;
        }
        // The code from the join to the outer join is the switch's too: only the cases' code, and the cases that branch
        // to the join itself, enter it.
        if (meeting is { } twoLevels && twoLevels.Join != twoLevels.Outer &&
            ClosedRegions.Reaches(state, twoLevels.Join,
                new HashSet<int>([.. regionBlocks, .. entries.GetValueOrDefault(twoLevels.Join) ?? []]), twoLevels.Outer,
                allowed, terminals, reserved) is null)
        {
            return notHandled;
        }

        var table = Claim(state, chain);
        var nested = new StructuredSequenceDraft([table]);
        foreach (var target in targets.Where(target => target != join && target != outer))
        {
            var body = structuredControlFlowBuilder.Build(state, target, destinations[target], allowed, [.. path]);
            nested = new StructuredSequenceDraft([
                new StructuredLabeledBlockDraft(target, nested),
                .. body.Regions,
                .. reaches[target] && destinations[target] is int reached
                    ? [new StructuredLabelBranchDraft(reached)]
                    : Array.Empty<StructuredRegionDraft>(),
            ]);
        }
        if (meeting is null)
        {
            regions.AddRange(nested.Regions);
        }
        else if (meeting.Join == meeting.Outer)
        {
            regions.Add(new StructuredLabeledBlockDraft(meeting.Join, nested));
        }
        else
        {
            var between = structuredControlFlowBuilder.Build(state, meeting.Join, meeting.Outer, allowed, [.. path]);
            nested = new StructuredSequenceDraft([new StructuredLabeledBlockDraft(meeting.Join, nested), .. between.Regions]);
            regions.Add(new StructuredLabeledBlockDraft(meeting.Outer, nested));
        }
        return new StructuredSequenceStepResult(StructuredSequenceStepDisposition.Continue, outer);
    }

    // The step owns a chain whose blocks are in the allowed blocks, not reserved and not the stop. A region already
    // holding some of them (a loop whose condition is one of its compares) leaves the switch none; one holding all of
    // them is a copy.
    private static bool Owns(ControlFlowStructuringState state, SwitchChain chain, int? stop, ImmutableHashSet<int> allowed,
        ReservedBlocks reserved)
    {
        var claimed = chain.Blocks.Prepend(chain.Head).Count(block => state.ClaimedBlockBodies.Contains(block.Index));
        return !chain.Blocks.Any(block => !allowed.Contains(block.Index) || reserved.Contains(block.Index) || block.Index == stop) &&
            (claimed == 0 || claimed == chain.Blocks.Length + 1);
    }

    private static StructuredSwitchDraft Claim(ControlFlowStructuringState state, SwitchChain chain)
    {
        var isOriginal = true;
        foreach (var block in chain.Blocks.Prepend(chain.Head))
        {
            isOriginal &= state.ClaimedBlockBodies.Add(block.Index);
            if (state.BoundaryOwnedBlocks.Contains(block.Index))
            {
                state.BoundaryClaimedBlockBodies.Add(block.Index);
            }
        }
        return new StructuredSwitchDraft(chain.Head, chain.Blocks, chain.Cases, chain.Default) { IsOriginal = isOriginal };
    }
}
