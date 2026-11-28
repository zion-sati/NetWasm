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
    ITerminatingExitJoinFinder terminatingJoins,
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
    private readonly ITerminatingExitJoinFinder _terminatingJoins = terminatingJoins ??
        throw new ArgumentNullException(nameof(terminatingJoins));
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
        ControlFlowDomains domains)
    {
        if (Header(_entryTargets.Find(graph, component, domain, domains), component, domain) is not int header)
        {
            throw _errors.Create(state, "entry loop has an additional external entry");
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
        var created = Region(exit);
        // Exits that meet only once the paths ending the region are set aside have no common post-dominator, so the
        // condition's exit was taken and the rest of the exits' code fell into the body's extension. Where that leaves
        // a dispatcher, the loop is tried again with the exit they meet at (TerminatingExitJoinFinder).
        if (created.Region is null && exits.Count > 1 &&
            _terminatingJoins.Find(state.ContinuationGraph, [.. exits.Order()], component, domain.Blocks) is int join &&
            join != exit &&
            Region(join) is { Region: not null } joined)
        {
            return joined;
        }
        return created;

        LoopRegionCreation Region(int exit)
        {
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
    }

    // The loop's header from the blocks its domain enters it at (ILoopEntryTargetFinder): its domain's entry (the
    // method's, or a handler's when the loop starts the handler) for a loop that holds it and has no other external
    // entry (null when it has one), or the one block entered from outside the loop.
    internal static int? Header(
        ImmutableHashSet<int> entryTargets,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain)
    {
        if (component.Contains(domain.Entry))
        {
            return entryTargets.IsEmpty ? domain.Entry : null;
        }
        // Natural-loop analysis guarantees one external header for every
        // reachable component that does not hold its domain's entry.
        return entryTargets.Single();
    }

    private LoopRegionCreation? CreateUnconditional(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        int header,
        ControlFlowDomain domain,
        ControlFlowDomains domains)
    {
        // Protected regions entered inside the loop. Their blocks belong to the
        // loop body, and their leaves reach the loop's continue and exit targets
        // through the regions' continuations.
        // The regions are those starting at the loop's blocks, and their blocks those their ranges hold, from the
        // graph's index: a scan of every region and every block for each loop took the method's loops times its size.
        var headerOffset = graph.GetBlock(header).StartOffset;
        var regions = graph.RegionBlocks;
        var nested = component
            .SelectMany(regions.TryRegionsStartingAt)
            .Where(region => !Contains(region.TryOffset, region.TryLength, headerOffset))
            .ToArray();
        if (nested.Length == 0)
        {
            return null;
        }
        // Nested protected blocks hold their inner ones, so the blocks are those of the merged ranges, each once.
        var protectedBlocks = Merged(nested.Select(region => (region.TryOffset, region.TryOffset + region.TryLength)))
            .SelectMany(range => regions.BlocksFrom(range.Start, range.End - range.Start))
            .Where(graph.ReachableBlocks.Contains)
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
        return graph.RegionBlocks.RangesAroundAlsoHold(condition, graph.GetBlock(header).StartOffset);
    }

    // The ranges' union as ranges apart from each other, in order.
    private static IEnumerable<(int Start, int End)> Merged(IEnumerable<(int Start, int End)> ranges)
    {
        (int Start, int End)? open = null;
        foreach (var range in ranges.OrderBy(range => range.Start))
        {
            if (open is { } current && range.Start <= current.End)
            {
                open = (current.Start, Math.Max(current.End, range.End));
                continue;
            }
            if (open is { } done)
            {
                yield return done;
            }
            open = range;
        }
        if (open is { } last)
        {
            yield return last;
        }
    }

    private static bool Contains(int offset, int length, int candidate) =>
        candidate >= offset && candidate < offset + length;
}
