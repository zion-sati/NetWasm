// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class BranchJoinSelector(
    ICommonReachableBlockFinder joins,
    IReachableBlockFinder reachableBlocks,
    IControlFlowDistanceFinder distances,
    IBranchReachabilityOverlapClassifier overlaps,
    IControlFlowSubgraphCycleClassifier cycles,
    BranchPrefixDuplicationPolicy duplicationPolicy =
        BranchPrefixDuplicationPolicy.SizeAware) : IBranchJoinSelector
{
    private const int SizeAwareMaximumDuplicatedPrefixBlockCount = 3;

    private readonly ICommonReachableBlockFinder _joins = joins ??
        throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ??
        throw new ArgumentNullException(nameof(reachableBlocks));
    private readonly IControlFlowDistanceFinder _distances = distances ??
        throw new ArgumentNullException(nameof(distances));
    private readonly IBranchReachabilityOverlapClassifier _overlaps = overlaps ??
        throw new ArgumentNullException(nameof(overlaps));
    private readonly IControlFlowSubgraphCycleClassifier _cycles = cycles ??
        throw new ArgumentNullException(nameof(cycles));
    private readonly int? _maximumDuplicatedPrefixBlockCount =
        duplicationPolicy switch
        {
            BranchPrefixDuplicationPolicy.Disabled => 0,
            BranchPrefixDuplicationPolicy.SizeAware =>
                SizeAwareMaximumDuplicatedPrefixBlockCount,
            BranchPrefixDuplicationPolicy.Speed => null,
            _ => throw new ArgumentOutOfRangeException(nameof(duplicationPolicy)),
        };

    public BranchJoinSelection Select(BranchJoinSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.State);
        ArgumentNullException.ThrowIfNull(request.AllowedBlocks);

        var terminalTransfers = ImmutableHashSet.CreateBuilder<int>();
        if (request.ContinueTarget is int continueTarget)
        {
            terminalTransfers.Add(continueTarget);
        }
        if (request.BreakTarget is int breakTarget)
        {
            terminalTransfers.Add(breakTarget);
        }
        var terminals = terminalTransfers.ToImmutable();
        var analysisBlocks = request.AllowedBlocks.Except(terminals);
        var strictJoin = _joins.Find(
            request.State,
            request.First,
            request.Second,
            request.Stop,
            analysisBlocks);
        var initialJoin = IsValidStop(
                request,
                analysisBlocks,
                terminals)
            ? request.Stop
            : strictJoin;
        if (!_overlaps.Classify(
                request.State,
                request.First,
                request.Second,
                initialJoin,
                analysisBlocks))
        {
            return new(initialJoin, analysisBlocks, terminals, []);
        }

        var candidate = FindCandidate(request, analysisBlocks, terminals);
        return candidate ?? new(initialJoin, analysisBlocks, terminals, []);
    }

    private bool IsValidStop(
        BranchJoinSelectionRequest request,
        ImmutableHashSet<int> analysisBlocks,
        ImmutableHashSet<int> terminalTransfers)
    {
        if (request.Stop is not int stop)
        {
            return false;
        }
        var firstPrefix = _reachableBlocks.Find(
            request.State,
            request.First,
            stop,
            analysisBlocks);
        var secondPrefix = _reachableBlocks.Find(
            request.State,
            request.Second,
            stop,
            analysisBlocks);
        var firstReachesStop = ReachesStop(
            request.State,
            request.First,
            stop,
            firstPrefix);
        var secondReachesStop = ReachesStop(
            request.State,
            request.Second,
            stop,
            secondPrefix);
        return (firstReachesStop || secondReachesStop) &&
            IsClosedPrefix(
                request,
                request.First,
                stop,
                firstPrefix,
                analysisBlocks,
                terminalTransfers) &&
            IsClosedPrefix(
                request,
                request.Second,
                stop,
                secondPrefix,
                analysisBlocks,
                terminalTransfers);
    }

    private static bool ReachesStop(
        ControlFlowStructuringState state,
        int start,
        int stop,
        ImmutableHashSet<int> prefix) =>
        start == stop ||
        prefix.Any(block => state.Graph.Successors[block].Contains(stop));

    private static bool IsClosedPrefix(
        BranchJoinSelectionRequest request,
        int start,
        int stop,
        ImmutableHashSet<int> prefix,
        ImmutableHashSet<int> analysisBlocks,
        ImmutableHashSet<int> terminalTransfers)
    {
        if (start == stop || terminalTransfers.Contains(start))
        {
            return true;
        }
        if (!analysisBlocks.Contains(start))
        {
            return false;
        }
        foreach (var block in prefix)
        {
            foreach (var successor in request.State.Graph.Successors[block])
            {
                if (successor == stop ||
                    prefix.Contains(successor) ||
                    (request.Stop == stop && !request.AllowedBlocks.Contains(successor)) ||
                    terminalTransfers.Contains(successor))
                {
                    continue;
                }
                return false;
            }
        }
        return true;
    }

    private BranchJoinSelection? FindCandidate(
        BranchJoinSelectionRequest request,
        ImmutableHashSet<int> analysisBlocks,
        ImmutableHashSet<int> terminalTransfers)
    {
        var firstDistances = _distances.Find(
            request.State,
            request.First,
            request.Stop,
            analysisBlocks);
        var secondDistances = _distances.Find(
            request.State,
            request.Second,
            request.Stop,
            analysisBlocks);
        return firstDistances.Keys
            .Intersect(secondDistances.Keys)
            .Select(candidate => CreateCandidate(
                request,
                candidate,
                analysisBlocks,
                terminalTransfers))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.DuplicatedPrefixBlocks.Count)
            .ThenBy(candidate =>
                firstDistances[candidate.Join!.Value] +
                secondDistances[candidate.Join.Value])
            .ThenBy(candidate => Math.Max(
                firstDistances[candidate.Join!.Value],
                secondDistances[candidate.Join.Value]))
            .ThenBy(candidate => candidate.Join)
            .FirstOrDefault();
    }

    private BranchJoinSelection? CreateCandidate(
        BranchJoinSelectionRequest request,
        int candidate,
        ImmutableHashSet<int> analysisBlocks,
        ImmutableHashSet<int> terminalTransfers)
    {
        var firstPrefix = _reachableBlocks.Find(
            request.State,
            request.First,
            candidate,
            analysisBlocks);
        var secondPrefix = _reachableBlocks.Find(
            request.State,
            request.Second,
            candidate,
            analysisBlocks);
        // Candidate-bounded walks must not absorb the caller's continuation.
        // The enclosing sequence still owns and emits everything from its stop.
        if (request.Stop is int stop &&
            (firstPrefix.Contains(stop) || secondPrefix.Contains(stop)))
        {
            return null;
        }
        var continuation = _reachableBlocks.Find(
            request.State,
            candidate,
            request.Stop,
            analysisBlocks);
        if (!IsClosedPrefix(
                request,
                request.First,
                candidate,
                firstPrefix,
                analysisBlocks,
                terminalTransfers) ||
            !IsClosedPrefix(
                request,
                request.Second,
                candidate,
                secondPrefix,
                analysisBlocks,
                terminalTransfers) ||
            firstPrefix.Overlaps(continuation) ||
            secondPrefix.Overlaps(continuation))
        {
            return null;
        }

        var duplicated = firstPrefix.Intersect(secondPrefix);
        if ((_maximumDuplicatedPrefixBlockCount is int maximumDuplicatedPrefixBlockCount &&
            duplicated.Count > maximumDuplicatedPrefixBlockCount) ||
            !CanDuplicate(
                request,
                duplicated,
                firstPrefix.Union(secondPrefix),
                candidate,
                terminalTransfers))
        {
            return null;
        }

        return new(candidate, analysisBlocks, terminalTransfers, duplicated);
    }

    private bool CanDuplicate(
        BranchJoinSelectionRequest request,
        ImmutableHashSet<int> duplicated,
        ImmutableHashSet<int> prefixes,
        int join,
        ImmutableHashSet<int> terminalTransfers)
    {
        if (duplicated.IsEmpty)
        {
            return true;
        }
        if (!request.State.Graph.MethodBody.ExceptionRegions.IsEmpty ||
            duplicated.Any(request.State.LoopsByHeader.ContainsKey) ||
            duplicated.Any(request.State.DispatchersByNode.ContainsKey) ||
            _cycles.Classify(request.State.Graph, duplicated))
        {
            return false;
        }

        foreach (var block in duplicated)
        {
            // External predecessors are permitted because structured branches
            // create occurrences and the ownership projector selects one body
            // definition. The duplicated prefix must still be closed on its
            // outgoing side so one occurrence cannot escape into foreign flow.
            if (request.State.Graph.Successors[block].Any(successor =>
                    successor != join &&
                    !prefixes.Contains(successor) &&
                    !terminalTransfers.Contains(successor)))
            {
                return false;
            }
        }
        return true;
    }

}
