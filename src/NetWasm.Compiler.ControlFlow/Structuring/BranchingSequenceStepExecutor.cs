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
    IControlFlowDistanceFinder distances,
    IBranchReachabilityOverlapClassifier branchOverlaps,
    IDispatcherExitSelector dispatcherExits) : IStructuredSequenceStepExecutor
{
    private readonly ICommonReachableBlockFinder _joins = joins ??
        throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ??
        throw new ArgumentNullException(nameof(reachableBlocks));
    private readonly IControlFlowDistanceFinder _distances = distances ??
        throw new ArgumentNullException(nameof(distances));
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
        var successors = state.Graph.Successors[current.Value];

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
            var branchTarget = successors[0];
            var fallthrough = successors[1];
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
            var activeLoopExit = state.LoopExitTargets.TryPeek(out var breakTarget)
                ? breakTarget
                : (int?)null;
            var branchReachesStop = stop is int stopBlock &&
                (_distances.Find(state, trueStart, stop, allowed).ContainsKey(stopBlock) ||
                 _distances.Find(state, falseStart, stop, allowed).ContainsKey(stopBlock)) ||
                trueStart == activeLoopExit || falseStart == activeLoopExit;
            var join = branchReachesStop
                ? stop
                : _joins.Find(state, trueStart, falseStart, stop, allowed);
            ImmutableHashSet<int>? joinDispatcher = null;
            var dispatcherCandidate = join is int dispatcherJoinBlock
                && state.DispatchersByNode.TryGetValue(dispatcherJoinBlock, out joinDispatcher);
            var firstPath = dispatcherCandidate &&
                _reachableBlocks.Find(state, trueStart, join!.Value, allowed).Any(joinDispatcher!.Contains);
            var secondPath = dispatcherCandidate && !firstPath &&
                _reachableBlocks.Find(state, falseStart, join!.Value, allowed).Any(joinDispatcher!.Contains);
            var reachesDispatcher = firstPath || secondPath;
            if (reachesDispatcher)
            {
                var dispatcherExits = _dispatcherExits.Select(joinDispatcher!
                    .SelectMany(node => state.Graph.Successors[node]), allowed);
                join = _joins.Find(state, dispatcherExits, stop, allowed);
            }
            if (_branchOverlaps.Classify(state,
                        trueStart,
                        falseStart,
                        join,
                        allowed))
            {
                var component = _reachableBlocks.Find(state,
                            current.Value,
                            join,
                            allowed);
                var dispatcherJoin = _joins.Find(state,
                    component
                        .SelectMany(node => state.Graph.Successors[node])
                        .Where(successor => !component.Contains(successor))
                        .Where(allowed.Contains)
                        .Distinct()
                        .Order()
                        .ToArray(),
                    join,
                    allowed);
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
            var isOriginal = state.ClaimedBlockBodies.Add(block.Index);
            if (state.BoundaryOwnedBlocks.Contains(block.Index))
            {
                state.BoundaryClaimedBlockBodies.Add(block.Index);
            }
            regions.Add(new StructuredIfDraft(block, whenTrue, whenFalse)
            {
                IsOriginal = isOriginal,
            });
            current = join;
            return new StructuredSequenceStepResult(
                StructuredSequenceStepDisposition.Continue,
                current);
        }

        return new StructuredSequenceStepResult(
            StructuredSequenceStepDisposition.NotHandled,
            current);
    }
}
