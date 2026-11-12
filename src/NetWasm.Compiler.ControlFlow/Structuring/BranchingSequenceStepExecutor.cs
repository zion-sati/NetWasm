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
    IDispatcherExitSelector dispatcherExits) : IStructuredSequenceStepExecutor
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

        if (block.Terminator.Operation is
            CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
                CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or
                CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or
                CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or
                CilOperation.BranchIfLessThanOrEqualUnsigned)
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
            var trueStart = block.Terminator.Operation is CilOperation.BranchIfTrue or
                CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or
                CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or
                CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or
                CilOperation.BranchIfLessThanOrEqualUnsigned
                ? branchTarget
                : fallthrough;
            var falseStart = block.Terminator.Operation is CilOperation.BranchIfTrue or
                CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or
                CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or
                CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or
                CilOperation.BranchIfLessThanOrEqualUnsigned
                ? fallthrough
                : branchTarget;
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
            if (!hasApplicableSplitProof &&
                _branchOverlaps.Classify(state,
                        trueStart,
                        falseStart,
                        join,
                        analysisAllowed))
            {
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
            var whenTrue = structuredControlFlowBuilder.Build(state,
                trueStart,
                join,
                allowed,
                [.. path]);
            var whenFalse = structuredControlFlowBuilder.Build(state,
                falseStart,
                join,
                allowed,
                [.. path]);
            AddConditional(state, block, whenTrue, whenFalse, regions);
            current = join;
            return new StructuredSequenceStepResult(
                StructuredSequenceStepDisposition.Continue,
                current);
        }

        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.NotHandled,
            current);
    }

    private static void AddConditional(
        ControlFlowStructuringState state,
        BasicBlock block,
        StructuredSequenceDraft whenTrue,
        StructuredSequenceDraft whenFalse,
        ImmutableArray<StructuredRegionDraft>.Builder regions)
    {
        var isOriginal = state.ClaimedBlockBodies.Add(block.Index);
        if (state.BoundaryOwnedBlocks.Contains(block.Index))
        {
            state.BoundaryClaimedBlockBodies.Add(block.Index);
        }
        regions.Add(new StructuredIfDraft(block, whenTrue, whenFalse)
        {
            IsOriginal = isOriginal,
        });
    }
}
