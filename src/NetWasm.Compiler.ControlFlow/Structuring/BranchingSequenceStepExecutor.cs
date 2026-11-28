// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class BranchingSequenceStepExecutor(
    ICommonReachableBlockFinder joins,
    IReachableBlockFinder reachableBlocks,
    IBranchJoinSelector branchJoins,
    IBranchReachabilityOverlapClassifier branchOverlaps,
    IDispatcherExitSelector dispatcherExits,
    IElseIfChainPolicy elseIfChains) : IStructuredSequenceStepExecutor
{
    private readonly ICommonReachableBlockFinder _joins = joins ??
        throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ??
        throw new ArgumentNullException(nameof(reachableBlocks));
    private readonly IBranchJoinSelector _branchJoins = branchJoins ??
        throw new ArgumentNullException(nameof(branchJoins));
    private readonly IBranchReachabilityOverlapClassifier _branchOverlaps = branchOverlaps ??
        throw new ArgumentNullException(nameof(branchOverlaps));
    private readonly IDispatcherExitSelector _dispatcherExits =
        dispatcherExits ?? throw new ArgumentNullException(nameof(dispatcherExits));
    private readonly IElseIfChainPolicy _elseIfChains =
        elseIfChains ?? throw new ArgumentNullException(nameof(elseIfChains));

    // How a conditional block splits: where each arm starts, where they rejoin, the blocks the join analysis saw, and
    // whether the arms overlap so that the branch must be structured as a dispatcher.
    private sealed record Branch(int TrueStart, int FalseStart, int? Join, ImmutableHashSet<int> AnalysisBlocks,
        bool Overlaps);

    public StructuredSequenceStepResult Execute(
        IStructuredControlFlowBuilder structuredControlFlowBuilder,
        ControlFlowStructuringState state,
        int currentBlockOffset,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path,
        ImmutableArray<StructuredRegionDraft>.Builder regions)
    {
        int? current = currentBlockOffset;

        var block = state.Graph.GetBlock(current.Value);

        if (IsConditional(block))
        {
            var (branchTarget, fallthrough) = state.Graph.GetConditionalTargets(current.Value);
            if (branchTarget == fallthrough)
            {
                // Both edges reach the next block, as a branch to the next instruction does: the
                // condition is still evaluated, and consumed by an if whose arms are empty.
                AddConditional(state, block, StructuredSequenceDraft.Empty, StructuredSequenceDraft.Empty, regions);
                return new StructuredSequenceStepResult(
                    StructuredSequenceStepDisposition.Continue,
                    branchTarget);
            }
            var branch = Plan(state, current.Value, stop, allowed);
            var join = branch.Join;
            if (branch.Overlaps)
            {
                var analysisAllowed = branch.AnalysisBlocks;
                var component = _reachableBlocks.Find(state,
                            current.Value,
                            join,
                            // The current branch can also be a loop-continue
                            // target. It must seed the dispatcher even though
                            // join analysis excludes terminal transfers.
                            analysisAllowed.Add(current.Value));
                var dispatcherJoin = _joins.Find(state,
                    component
                        .SelectMany(node => state.Graph.Successors[node])
                        .Where(successor => !component.Contains(successor))
                        .Where(analysisAllowed.Contains)
                        .Distinct()
                        .Order()
                        .ToArray(),
                    join,
                    analysisAllowed);
                regions.Add(structuredControlFlowBuilder.Build(
                    state,
                    current.Value,
                    component,
                    allowed,
                    dispatcherJoin,
                    [.. path]));
                current = dispatcherJoin;
                return new StructuredSequenceStepResult(
                    StructuredSequenceStepDisposition.Continue,
                    current);
            }
            // An else-if chain: while one arm of the condition is a closed region and the other starts the next
            // condition, entered from this one alone, that condition joins the chain here instead of being reached by
            // recursing into the arm. Each arm is walked once, so a chain of any length costs its size, and the chain
            // ends where the next condition does not qualify; what follows is structured as the recursion would.
            if (join is int chainJoin && Links(state, block, branch, chainJoin, allowed) is { Count: > 0 } links)
            {
                regions.Add(Chain(structuredControlFlowBuilder, state, chainJoin, links, allowed, path));
            }
            else
            {
                var whenTrue = structuredControlFlowBuilder.Build(state,
                    branch.TrueStart,
                    join,
                    allowed,
                    [.. path]);
                var whenFalse = structuredControlFlowBuilder.Build(state,
                    branch.FalseStart,
                    join,
                    allowed,
                    [.. path]);
                AddConditional(state, block, whenTrue, whenFalse, regions);
            }
            current = join;
            return new StructuredSequenceStepResult(
                StructuredSequenceStepDisposition.Continue,
                current);
        }

        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.NotHandled,
            current);
    }

    // One condition of an else-if chain: its block, its arm that is a closed region (and whether that region reaches
    // the join), which arm continues the chain, and the start of the continuing arm.
    private sealed record Link(BasicBlock Block, int BodyStart, bool BodyReachesJoin, bool ContinuesWhenTrue,
        int Continuation);

    // The chain from the condition on: each link's other arm starts the next, until a condition does not qualify.
    private static List<Link> Links(
        ControlFlowStructuringState state,
        BasicBlock block,
        Branch branch,
        int join,
        ImmutableHashSet<int> allowed)
    {
        var links = new List<Link>();
        var terminals = ClosedRegions.Terminals(state);
        var reserved = ClosedRegions.Reserved(state, terminals);
        var (trueStart, falseStart) = (branch.TrueStart, branch.FalseStart);
        while (true)
        {
            Link? link = null;
            if (StartsLink(state, trueStart, block.Index, join, allowed, reserved) &&
                ClosedArm(state, falseStart, block.Index, join, allowed, terminals, reserved) is bool falseReaches)
            {
                link = new(block, falseStart, falseReaches, ContinuesWhenTrue: true, trueStart);
            }
            else if (StartsLink(state, falseStart, block.Index, join, allowed, reserved) &&
                ClosedArm(state, trueStart, block.Index, join, allowed, terminals, reserved) is bool trueReaches)
            {
                link = new(block, trueStart, trueReaches, ContinuesWhenTrue: false, falseStart);
            }
            if (link is null)
            {
                return links;
            }
            links.Add(link);
            block = state.Graph.GetBlock(link.Continuation);
            var (branchTarget, fallthrough) = state.Graph.GetConditionalTargets(block.Index);
            (trueStart, falseStart) = JumpsWhenTrue(block) ? (branchTarget, fallthrough) : (fallthrough, branchTarget);
        }
    }

    // The chain's bodies built in order, then what follows its last link up to the join, as the recursion would build
    // that arm; either nested, each later condition in the arm of the one before that continues the chain, or flat: a
    // labeled block that ends at the join, each link an if whose body branches out of it when it reaches the join,
    // followed by what follows the last link. Conditions are claimed innermost first, as the recursion claimed them.
    // Each arm and what follows the last link are built with the path the chain started with: the recursion added
    // each condition to it, but an arm, a closed region, reaches no condition, and what follows the last link reaches
    // one only through the chain's first condition, which the path holds; a path per link would cost the chain's
    // length squared.
    private StructuredRegionDraft Chain(
        IStructuredControlFlowBuilder builder,
        ControlFlowStructuringState state,
        int join,
        List<Link> links,
        ImmutableHashSet<int> allowed,
        HashSet<int> path)
    {
        var flat = _elseIfChains.Flattens(links.Count);
        var bodies = links.Select(link => builder.Build(state, link.BodyStart, join, allowed, [.. path])).ToArray();
        var last = links[^1];
        var remainder = builder.Build(state, last.Continuation, join, allowed, [.. path]);
        var conditionals = new StructuredRegionDraft[links.Count];
        var rest = remainder;
        for (var index = links.Count - 1; index >= 0; index--)
        {
            var link = links[index];
            var body = flat && link.BodyReachesJoin
                ? new StructuredSequenceDraft([.. bodies[index].Regions, new StructuredLabelBranchDraft(join)])
                : bodies[index];
            var continuation = flat ? StructuredSequenceDraft.Empty : rest;
            var conditional = link.ContinuesWhenTrue
                ? Conditional(state, link.Block, continuation, body)
                : Conditional(state, link.Block, body, continuation);
            conditionals[index] = conditional;
            rest = new StructuredSequenceDraft([conditional]);
        }
        return flat
            ? new StructuredLabeledBlockDraft(join, new StructuredSequenceDraft([.. conditionals, .. remainder.Regions]))
            : conditionals[0];
    }

    // A block that would start a further link: a conditional with two targets, entered only from the condition before
    // it, in the allowed blocks, not the join and not reserved.
    private static bool StartsLink(
        ControlFlowStructuringState state,
        int start,
        int previous,
        int join,
        ImmutableHashSet<int> allowed,
        ReservedBlocks reserved)
    {
        if (start == join ||
            !allowed.Contains(start) ||
            state.Graph.Predecessors[start] is not [var only] || only != previous ||
            reserved.Contains(start) ||
            !IsConditional(state.Graph.GetBlock(start)))
        {
            return false;
        }
        var (branchTarget, fallthrough) = state.Graph.GetConditionalTargets(start);
        return branchTarget != fallthrough;
    }

    // An arm that is a closed region entered only from the condition: whether it reaches the join, or null.
    private static bool? ClosedArm(
        ControlFlowStructuringState state,
        int start,
        int condition,
        int join,
        ImmutableHashSet<int> allowed,
        ImmutableHashSet<int> terminals,
        ReservedBlocks reserved) =>
        ClosedRegions.Reaches(state, start, new HashSet<int> { condition }, join, allowed, terminals, reserved);

    private Branch Plan(ControlFlowStructuringState state, int current, int? stop, ImmutableHashSet<int> allowed)
    {
        var block = state.Graph.GetBlock(current);
        var (branchTarget, fallthrough) = state.Graph.GetConditionalTargets(current);
        var trueStart = JumpsWhenTrue(block) ? branchTarget : fallthrough;
        var falseStart = JumpsWhenTrue(block) ? fallthrough : branchTarget;
        var selection = _branchJoins.Select(new(
            state,
            trueStart,
            falseStart,
            stop,
            allowed,
            state.LoopContinueTargets.TryPeek(out var continueTarget)
                ? continueTarget
                : null,
            state.LoopExitTargets.TryPeek(out var breakTarget)
                ? breakTarget
                : null));
        var join = selection.Join;
        var analysisAllowed = selection.AnalysisBlocks;
        ImmutableHashSet<int>? joinDispatcher = null;
        var dispatcherCandidate = join is int dispatcherJoinBlock
            && state.DispatchersByNode.TryGetValue(dispatcherJoinBlock, out joinDispatcher);
        var firstPath = dispatcherCandidate &&
            _reachableBlocks.Find(state, trueStart, join!.Value, analysisAllowed).Any(joinDispatcher!.Contains);
        var secondPath = dispatcherCandidate && !firstPath &&
            _reachableBlocks.Find(state, falseStart, join!.Value, analysisAllowed).Any(joinDispatcher!.Contains);
        var reachesDispatcher = firstPath || secondPath;
        if (reachesDispatcher)
        {
            var dispatcherExits = _dispatcherExits.Select(joinDispatcher!
                .SelectMany(node => state.Graph.Successors[node]), analysisAllowed);
            join = _joins.Find(state, dispatcherExits, stop, analysisAllowed);
        }
        var hasApplicableSplitProof =
            !selection.DuplicatedPrefixBlocks.IsEmpty &&
            !reachesDispatcher;
        var overlaps = !hasApplicableSplitProof &&
            _branchOverlaps.Classify(state,
                trueStart,
                falseStart,
                join,
                analysisAllowed);
        return new Branch(trueStart, falseStart, join, analysisAllowed, overlaps);
    }

    private static bool IsConditional(BasicBlock block) =>
        block.Terminator.Operation is
            CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or
            CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or
            CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or
            CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or
            CilOperation.BranchIfLessThanOrEqualUnsigned;

    // Every conditional branch but brfalse jumps when its condition holds.
    private static bool JumpsWhenTrue(BasicBlock block) => block.Terminator.Operation != CilOperation.BranchIfFalse;

    private static void AddConditional(
        ControlFlowStructuringState state,
        BasicBlock block,
        StructuredSequenceDraft whenTrue,
        StructuredSequenceDraft whenFalse,
        ImmutableArray<StructuredRegionDraft>.Builder regions) =>
        regions.Add(Conditional(state, block, whenTrue, whenFalse));

    private static StructuredIfDraft Conditional(
        ControlFlowStructuringState state,
        BasicBlock block,
        StructuredSequenceDraft whenTrue,
        StructuredSequenceDraft whenFalse)
    {
        var isOriginal = state.ClaimedBlockBodies.Add(block.Index);
        if (state.BoundaryOwnedBlocks.Contains(block.Index))
        {
            state.BoundaryClaimedBlockBodies.Add(block.Index);
        }
        return new StructuredIfDraft(block, whenTrue, whenFalse)
        {
            IsOriginal = isOriginal,
        };
    }
}
