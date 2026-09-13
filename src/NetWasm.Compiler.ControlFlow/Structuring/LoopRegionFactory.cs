using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopRegionFactory(
    IReachableSetOverlapClassifier reachableSetOverlaps,
    ILoopConditionChooser loopConditions,
    ICommonReachableBlockFinder joins,
    IReachableBlockFinder reachableBlocks,
    IIrreducibleControlFlowExceptionFactory errors,
    ILoopExitPartitioner exitPartitions,
    ILoopContinueTargetFinder continueTargets) : ILoopRegionFactory
{
    private readonly IReachableSetOverlapClassifier _reachableSetOverlaps = reachableSetOverlaps ?? throw new ArgumentNullException(nameof(reachableSetOverlaps));
    private readonly ILoopConditionChooser _loopConditions = loopConditions ?? throw new ArgumentNullException(nameof(loopConditions));
    private readonly ICommonReachableBlockFinder _joins = joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));
    private readonly IIrreducibleControlFlowExceptionFactory _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    private readonly ILoopExitPartitioner _exitPartitions = exitPartitions ?? throw new ArgumentNullException(nameof(exitPartitions));
    private readonly ILoopContinueTargetFinder _continueTargets = continueTargets ?? throw new ArgumentNullException(nameof(continueTargets));

    public LoopRegionCreation Create(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain,
        ImmutableArray<ControlFlowDomain> domains)
    {
        var entryTargets = component
            .Where(node => graph.Predecessors[node].Any(predecessor =>
                !component.Contains(predecessor) &&
                !IsExceptionDomainPredecessor(predecessor)))
            .ToImmutableHashSet();
        int header;
        if (component.Contains(domain.Entry))
        {
            if (!entryTargets.IsEmpty)
            {
                throw _errors.Create(state, "entry loop has an additional external entry");
            }
            header = graph.Entry.Index;
        }
        else
        {
            // Natural-loop analysis guarantees one external header for every
            // reachable component outside the method entry domain.
            header = entryTargets.Single();
        }

        var condition = _loopConditions.Choose(new(graph, component, header));
        if (condition is null)
        {
            return new LoopRegionCreation(null, component);
        }
        var conditionIndex = condition.Value;
        if (conditionIndex != header)
        {
            var continuationStart = graph.Successors[conditionIndex]
                .Single(component.Contains);
            var bodyBlocks = _reachableBlocks.Find(state,
                header,
                conditionIndex,
                component);
            var continuationBlocks = _reachableBlocks.Find(state,
                continuationStart,
                header,
                component);
            if (_reachableSetOverlaps.Overlaps([bodyBlocks, continuationBlocks]))
            {
                // The selected exit condition does not partition the SCC into
                // single-owner loop halves. A whole-component dispatcher keeps
                // shared continuations canonical and prevents code duplication.
                return new LoopRegionCreation(null, component);
            }
        }
        var conditionExit = graph.Successors[conditionIndex]
            .Single(successor => !component.Contains(successor));
        var exits = component
            .SelectMany(node => graph.Successors[node])
            .Where(successor => !component.Contains(successor))
            .ToImmutableHashSet();
        var additionalExits = exits.Remove(conditionExit);
        var exit = _joins.Find(state,
            [.. exits.Order()],
            stop: null,
            domain.Blocks) ?? conditionExit;
        var exitPartition = _exitPartitions.Partition(
            graph,
            component,
            exit,
            conditionExit,
            additionalExits);
        if (exitPartition.Overlaps)
        {
            return new LoopRegionCreation(
                null,
                exitPartition.BodyComponent.Union(
                    exitPartition.ConditionExitExtension));
        }
        var continueTarget = conditionIndex == header
            ? _continueTargets.Find(state, graph, component, header)
            : header;
        return new LoopRegionCreation(
            new LoopRegion(
                header,
                conditionIndex,
                continueTarget,
                conditionExit,
                exit,
                component,
                exitPartition.BodyComponent,
                exitPartition.ConditionExitExtension),
            ImmutableHashSet<int>.Empty);

        bool IsExceptionDomainPredecessor(int predecessor) =>
            domain.Entry == graph.Entry.Index &&
            domains.Any(candidate =>
                candidate.Entry != graph.Entry.Index &&
                candidate.Blocks.Contains(predecessor));
    }

}
