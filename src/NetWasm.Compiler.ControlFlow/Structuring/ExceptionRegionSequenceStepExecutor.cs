using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionRegionSequenceStepExecutor(
    IReachableSetOverlapClassifier reachableSetOverlaps,
    IDispatcherBoundaryClipper dispatcherBoundaries,
    ICommonReachableBlockFinder joins,
    IReachableBlockFinder reachableBlocks) : IStructuredSequenceStepExecutor
{
    private readonly IReachableSetOverlapClassifier _reachableSetOverlaps =
        reachableSetOverlaps ?? throw new ArgumentNullException(nameof(reachableSetOverlaps));
    private readonly IDispatcherBoundaryClipper _dispatcherBoundaries =
        dispatcherBoundaries ?? throw new ArgumentNullException(nameof(dispatcherBoundaries));
    private readonly ICommonReachableBlockFinder _joins =
        joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks =
        reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));

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

        if (state.ExceptionGroupsByEntry.TryGetValue(current.Value, out var exceptionGroup))
        {
            var fallthroughTarget = exceptionGroup.NormalContinuations.IsEmpty
                ? (int?)null
                : state.Graph.GetBlockAtOffset(
                    exceptionGroup.NormalContinuations[0].TargetOffset).Index;
            var dispatcherContinuations =
                exceptionGroup.ContinuationDispatcher is null &&
                state.ActiveDispatchers.TryPeek(
                    out var activeDispatcher)
                ? exceptionGroup.NormalContinuations
                    .Select((continuation, index) => new
                    {
                        Index = index,
                        Target = state.Graph.GetBlockAtOffset(
                            continuation.TargetOffset).Index,
                    })
                    .Where(continuation => activeDispatcher.Contains(continuation.Target))
                    .Select(continuation => continuation.Index)
                    .ToImmutableHashSet()
                : [];
            var fallthroughContinuation = fallthroughTarget is int target &&
                allowed.Contains(target) &&
                !path.Contains(target) &&
                !state.DispatchersByNode.ContainsKey(target) &&
                !dispatcherContinuations.Contains(0)
                    ? 0
                    : (int?)null;
            regions.Add(new StructuredExceptionRegionDraft(
                exceptionGroup,
                fallthroughContinuation)
            {
                DispatcherContinuations = dispatcherContinuations,
            });
            if (exceptionGroup.ContinuationDispatcher is { } continuationDispatcher)
            {
                regions.Add(continuationDispatcher);
                current = exceptionGroup.ContinuationJoinBlock;
            }
            else
            {
                current = fallthroughContinuation is int index
                    ? state.Graph.GetBlockAtOffset(
                        exceptionGroup.NormalContinuations[index].TargetOffset).Index
                    : null;
            }
            return new StructuredSequenceStepResult(
                StructuredSequenceStepDisposition.Continue,
                current);
        }

        if (state.DispatchersByNode.TryGetValue(current.Value, out var dispatcher))
        {
            dispatcher = _dispatcherBoundaries.Clip(dispatcher, allowed, stop);
            // Exception-region validation rejects direct branches across a
            // protected boundary. Leave edges terminate the current sequence,
            // so an SCC claimed by this table is always contained in `allowed`.
            var exitTargets = dispatcher
                .SelectMany(node => state.Graph.Successors[node])
                .Where(successor => !dispatcher.Contains(successor))
                .Distinct()
                .Order()
                .ToArray();
            var inRegionExits = exitTargets.Where(allowed.Contains).ToArray();
            // A dispatcher extension can own a leave block whose target is
            // beyond the protected region. Retain that target as an empty
            // dispatcher exit so emission leaves the dispatcher and allows
            // the exception-region cleanup to run.
            var join = _joins.Find(state,
                inRegionExits,
                stop,
                allowed);
            var dispatcherComponent = _reachableSetOverlaps.Overlaps(
                [.. inRegionExits.Select(exit =>
                    _reachableBlocks.Find(state, exit, join, allowed))])
                ? _reachableBlocks.Find(state, current.Value, join, allowed)
                : dispatcher;
            regions.Add(structuredControlFlowBuilder.Build(
                state,
                current.Value,
                dispatcherComponent,
                allowed,
                join,
                [.. path]));
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
