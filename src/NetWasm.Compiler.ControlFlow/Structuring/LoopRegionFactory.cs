// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopRegionFactory(
    IReachableSetOverlapClassifier reachableSetOverlaps,
    ILoopConditionChooser loopConditions,
    ICommonReachableBlockFinder joins,
    IReachableBlockFinder reachableBlocks,
    IIrreducibleControlFlowExceptionFactory errors,
    ILoopExitPartitioner exitPartitions,
    ILoopContinueTargetFinder continueTargets,
    ILoopRegionEligibilityClassifier eligibility,
    ILoopEntryTargetFinder entryTargets,
    BranchPrefixDuplicationPolicy duplicationPolicy = BranchPrefixDuplicationPolicy.SizeAware) : ILoopRegionFactory
{
    private readonly IReachableSetOverlapClassifier _reachableSetOverlaps = reachableSetOverlaps ?? throw new ArgumentNullException(nameof(reachableSetOverlaps));
    private readonly ILoopConditionChooser _loopConditions = loopConditions ?? throw new ArgumentNullException(nameof(loopConditions));
    private readonly ICommonReachableBlockFinder _joins = joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));
    private readonly IIrreducibleControlFlowExceptionFactory _errors = errors ?? throw new ArgumentNullException(nameof(errors));
    private readonly ILoopExitPartitioner _exitPartitions = exitPartitions ?? throw new ArgumentNullException(nameof(exitPartitions));
    private readonly ILoopContinueTargetFinder _continueTargets = continueTargets ?? throw new ArgumentNullException(nameof(continueTargets));
    private readonly ILoopRegionEligibilityClassifier _eligibility = eligibility ??
        throw new ArgumentNullException(nameof(eligibility));
    private readonly ILoopEntryTargetFinder _entryTargets = entryTargets ??
        throw new ArgumentNullException(nameof(entryTargets));
    private readonly bool _allowSharedExitSplitting = duplicationPolicy switch
    {
        BranchPrefixDuplicationPolicy.Disabled or BranchPrefixDuplicationPolicy.SizeAware => false,
        BranchPrefixDuplicationPolicy.Speed => true,
        _ => throw new ArgumentOutOfRangeException(nameof(duplicationPolicy)),
    };

    public LoopRegionCreation Create(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain,
        ImmutableArray<ControlFlowDomain> domains)
    {
        var entryTargets = _entryTargets.Find(graph, component, domain, domains);
        int header;
        if (component.Contains(domain.Entry))
        {
            if (!entryTargets.IsEmpty)
            {
                throw _errors.Create(state, "entry loop has an additional external entry");
            }
            // The method's entry, or a handler's when the loop starts the handler.
            header = domain.Entry;
        }
        else
        {
            // Natural-loop analysis guarantees one external header for every
            // reachable component that does not hold its domain's entry.
            header = entryTargets.Single();
        }

        var condition = _loopConditions.Choose(new(graph, component, header));
        if (condition is not int conditionIndex ||
            !EnclosesCondition(graph, header, conditionIndex))
        {
            // A branch inside a protected region the loop enters cannot be the
            // loop's condition: testing it at the loop would split it from its
            // region. Such loops leave only through those regions.
            return CreateUnconditional(state, graph, component, header, domain, domains) ??
                new LoopRegionCreation(null, component);
        }
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
            .SelectMany(node => state.ContinuationGraph.Successors[node])
            .Where(successor => !component.Contains(successor))
            .ToImmutableHashSet();
        var additionalExits = exits.Remove(conditionExit);
        var exit = _joins.Find(state,
            [.. exits.Order()],
            stop: null,
            domain.Blocks) ?? conditionExit;
        var exitPartition = _exitPartitions.Partition(
            // Catch continuations contribute real exit paths even when no
            // ordinary successor reaches them. Discovery, joins and extension
            // planning must use the same summary graph; emission still uses CIL.
            state.ContinuationGraph,
            component,
            exit,
            conditionExit,
            additionalExits);
        if (exitPartition.Overlaps)
        {
            // Keep the cyclic core canonical, but let the enclosing speed-policy
            // branches split shared exit code. Dispatcher emission still expands
            // its local component when its exits cannot be structured separately.
            return new LoopRegionCreation(
                null,
                _allowSharedExitSplitting
                    ? component
                    : exitPartition.BodyComponent.Union(
                        exitPartition.ConditionExitExtension));
        }
        var continueTarget = conditionIndex == header
            ? _continueTargets.Find(state, graph, component, header)
            : header;
        var region = new LoopRegion(
            header,
            conditionIndex,
            continueTarget,
            conditionExit,
            exit,
            component,
            exitPartition.BodyComponent,
            exitPartition.ConditionExitExtension);
        return _eligibility.Classify(new(
                graph,
                component,
                region,
                domain,
                domains))
            ? new LoopRegionCreation(region, ImmutableHashSet<int>.Empty)
            : new LoopRegionCreation(null, component);
    }

    private LoopRegionCreation? CreateUnconditional(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        int header,
        ControlFlowDomain domain,
        ImmutableArray<ControlFlowDomain> domains)
    {
        // Protected regions entered inside the loop. Their blocks belong to the
        // loop body, and their leaves reach the loop's continue and exit targets
        // through the regions' continuations.
        var headerOffset = graph.GetBlock(header).StartOffset;
        var nested = graph.MethodBody.ExceptionRegions
            .Where(region => component.Contains(graph.GetBlockAtOffset(region.TryOffset).Index) &&
                !Contains(region.TryOffset, region.TryLength, headerOffset))
            .ToArray();
        if (nested.Length == 0)
        {
            return null;
        }
        var protectedBlocks = graph.ReachableBlocks
            .Where(block => nested.Any(region => Contains(
                region.TryOffset,
                region.TryLength,
                graph.GetBlock(block).StartOffset)))
            .ToImmutableHashSet();
        var body = component.Union(protectedBlocks);
        var exits = ImmutableHashSet.CreateBuilder<int>();
        foreach (var block in body)
        {
            // Catch continuations of the regions entered here are exits; those
            // of a region enclosing the loop belong to that region's group.
            var successors = protectedBlocks.Contains(block)
                ? state.ContinuationGraph.Successors[block]
                : graph.Successors[block];
            foreach (var successor in successors)
            {
                if (body.Contains(successor))
                {
                    continue;
                }
                if (!protectedBlocks.Contains(block))
                {
                    // An ordinary exit needs a condition to test it.
                    return null;
                }
                exits.Add(successor);
            }
        }
        if (exits.Count == 0)
        {
            return null;
        }
        var exit = _joins.Find(state,
            [.. exits.Order()],
            stop: null,
            domain.Blocks) ?? exits.Min();
        // Every exit is an early exit, so the condition-exit extension is empty.
        var exitPartition = _exitPartitions.Partition(
            state.ContinuationGraph,
            body,
            exit,
            exit,
            exits.ToImmutable());
        if (exitPartition.Overlaps)
        {
            return null;
        }
        var region = new LoopRegion(
            header,
            null,
            header,
            exit,
            exit,
            component,
            exitPartition.BodyComponent,
            []);
        return _eligibility.Classify(new(
                graph,
                component,
                region,
                domain,
                domains))
            ? new LoopRegionCreation(region, ImmutableHashSet<int>.Empty)
            : null;
    }

    // Every protected region or handler that holds the condition holds the
    // header too, so testing the condition at the loop stays inside them.
    private static bool EnclosesCondition(ControlFlowGraph graph, int header, int condition)
    {
        var headerOffset = graph.GetBlock(header).StartOffset;
        var conditionOffset = graph.GetBlock(condition).StartOffset;
        return graph.MethodBody.ExceptionRegions.All(region =>
            (!Contains(region.TryOffset, region.TryLength, conditionOffset) ||
                Contains(region.TryOffset, region.TryLength, headerOffset)) &&
            (!Contains(region.HandlerOffset, region.HandlerLength, conditionOffset) ||
                Contains(region.HandlerOffset, region.HandlerLength, headerOffset)));
    }

    private static bool Contains(int offset, int length, int candidate) =>
        candidate >= offset && candidate < offset + length;
}
