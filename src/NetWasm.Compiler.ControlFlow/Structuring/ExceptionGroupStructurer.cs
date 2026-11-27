// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionGroupStructurer(
    IExceptionScopeFinder exceptionScopes,
    IContinuationDispatcherBuilder continuationDispatchers,
    ILoopContinuationBuilder loopContinuations,
    INestedFlowClassifier nestedFlows,
    IBlockRangeStructurer ranges,
    INormalLeaveTargetFinder leaveTargets,
    ITerminalContinuationFinder terminalContinuations,
    IContinuationExtentFinder continuationExtents) : IExceptionGroupStructurer
{
    private readonly IExceptionScopeFinder _exceptionScopes = exceptionScopes ?? throw new ArgumentNullException(nameof(exceptionScopes));
    private readonly IContinuationDispatcherBuilder _continuationDispatchers = continuationDispatchers ?? throw new ArgumentNullException(nameof(continuationDispatchers));
    private readonly ILoopContinuationBuilder _loopContinuations = loopContinuations ?? throw new ArgumentNullException(nameof(loopContinuations));
    private readonly INestedFlowClassifier _nestedFlows = nestedFlows ?? throw new ArgumentNullException(nameof(nestedFlows));
    private readonly IBlockRangeStructurer _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
    private readonly INormalLeaveTargetFinder _leaveTargets = leaveTargets ?? throw new ArgumentNullException(nameof(leaveTargets));
    private readonly ITerminalContinuationFinder _terminalContinuations = terminalContinuations ??
        throw new ArgumentNullException(nameof(terminalContinuations));
    private readonly IContinuationExtentFinder _continuationExtents = continuationExtents ??
        throw new ArgumentNullException(nameof(continuationExtents));

    public ImmutableArray<StructuredExceptionGroupDraft> Structure(ControlFlowStructuringState state,
        ImmutableArray<CilExceptionRegion> regions)
    {
        if (regions.IsEmpty)
        {
            return [];
        }
        // The reachable blocks' starts, in order: whether a try holds one is a binary search.
        var reachableStarts = state.Graph.Blocks
            .Where(block => state.Graph.ReachableBlocks.Contains(block.Index))
            .Select(block => block.StartOffset)
            .Order()
            .ToArray();
        var sources = regions
            // Keep all original regions in the CIL graph for lexical transfer
            // validation, but only executable protected code owns a group.
            .Where(region => HoldsReachableBlock(reachableStarts, region.TryOffset, region.TryOffset + region.TryLength))
            .GroupBy(region => (region.TryOffset, region.TryLength))
            .Select(group => new ExceptionGroupSource(
                group.Key.TryOffset,
                group.Key.TryLength,
                [.. group]))
            .ToArray();
        var parents = Parents(sources);
        // Each group's children in try order, and its index among the groups, from the parent map once.
        var childrenOf = new Dictionary<ExceptionGroupSource, List<ExceptionGroupSource>>();
        var sourceIndex = new Dictionary<ExceptionGroupSource, int>(sources.Length);
        for (var index = 0; index < sources.Length; index++)
        {
            sourceIndex[sources[index]] = index;
            if (!parents.TryGetValue(sources[index], out var parent)) continue;
            if (!childrenOf.TryGetValue(parent, out var list)) childrenOf[parent] = list = [];
            list.Add(sources[index]);
        }
        foreach (var list in childrenOf.Values)
        {
            var ordered = list.OrderBy(candidate => candidate.TryOffset).ToArray();
            list.Clear();
            list.AddRange(ordered);
        }
        // The groups a group holds: its descendants, which in valid IL are exactly the groups inside one of its
        // scopes, in the order of the groups.
        ExceptionGroupSource[] Descendants(ExceptionGroupSource source)
        {
            var found = new List<ExceptionGroupSource>();
            var pending = new Stack<ExceptionGroupSource>();
            pending.Push(source);
            while (pending.Count > 0)
            {
                if (!childrenOf.TryGetValue(pending.Pop(), out var children)) continue;
                foreach (var child in children)
                {
                    found.Add(child);
                    pending.Push(child);
                }
            }
            return [.. found.OrderBy(candidate => sourceIndex[candidate])];
        }
        var methodEnd = state.Graph.MethodBody.Instructions[^1].NextOffset;

        StructuredExceptionGroupDraft Build(
            ExceptionGroupSource source,
            int continuationBoundary)
        {
            var children = childrenOf.TryGetValue(source, out var ownChildren) ? ownChildren.ToArray() : [];
            var childScopes = children.ToDictionary(
                child => child,
                child => _exceptionScopes.Find(source, child)!.Value);
            // Per scope, its children's try starts in order, for the continuation boundaries.
            var siblingStarts = new Dictionary<ExceptionScope, List<int>>();
            foreach (var child in children)
            {
                if (!siblingStarts.TryGetValue(childScopes[child], out var starts)) siblingStarts[childScopes[child]] = starts = [];
                starts.Add(child.TryOffset);
            }
            var protectedChildren = children
                .Where(child => childScopes[child].Kind ==
                    ExceptionScopeKind.Protected)
                .ToArray();
            var nestedSources = Descendants(source);
            var handlerNestedSources = nestedSources
                .Where(candidate => _exceptionScopes.Find(source, candidate)!
                    .Value.Kind != ExceptionScopeKind.Protected)
                .ToArray();
            var leaveTargets = _leaveTargets.Find(state,
                source.Regions,
                nestedSources,
                handlerNestedSources);
            var parts = ImmutableArray.CreateBuilder<StructuredExceptionPartDraft>();
            var builtChildren = children.ToDictionary(
                child => child,
                child => Build(child, FindChildContinuationBoundary(child)));
            if (_nestedFlows.Classify(state, source, protectedChildren))
            {
                foreach (var child in protectedChildren)
                {
                    state.ExceptionGroupsByEntry.Add(
                        state.Graph.GetBlockAtOffset(child.TryOffset).Index,
                        builtChildren[child]);
                }
                try
                {
                    parts.Add(new StructuredExceptionCodeDraft(
                        _ranges.Structure(state, source.TryOffset, source.TryLength)));
                }
                finally
                {
                    foreach (var child in protectedChildren)
                    {
                        state.ExceptionGroupsByEntry.Remove(
                            state.Graph.GetBlockAtOffset(child.TryOffset).Index);
                    }
                }
            }
            else
            {
                var cursor = source.TryOffset;
                for (var index = 0; index < protectedChildren.Length; index++)
                {
                    var child = protectedChildren[index];
                    if (cursor < child.TryOffset)
                    {
                        parts.Add(new StructuredExceptionCodeDraft(
                            _ranges.Structure(state, cursor, child.TryOffset - cursor)));
                    }
                    var childBoundary = index + 1 < protectedChildren.Length
                        ? protectedChildren[index + 1].TryOffset
                        : source.TryEnd;
                    parts.Add(new StructuredNestedExceptionGroupDraft(builtChildren[child]));
                    cursor = childBoundary;
                }
                if (cursor < source.TryEnd)
                {
                    parts.Add(new StructuredExceptionCodeDraft(
                        _ranges.Structure(state, cursor, source.TryEnd - cursor)));
                }
            }
            var nestedProtectedPart =
                parents.TryGetValue(source, out var parent) &&
                _exceptionScopes.Find(parent, source)!.Value.Kind ==
                ExceptionScopeKind.Protected;
            var continuationEnds = new Dictionary<int, int>();
            var continuations = leaveTargets.Select((target, index) =>
            {
                var internalLoop = FindInternalLoop(source, target);
                // Falling out of the region continues the loop, so only a target
                // inside the loop may leave its first continuation empty; the
                // loop's exit needs an explicit break.
                if (index == 0 &&
                    internalLoop is not null &&
                    internalLoop.Exit != state.Graph.GetBlockAtOffset(target).Index &&
                    !nestedProtectedPart)
                {
                    return new StructuredExceptionContinuationDraft(
                        target,
                        StructuredSequenceDraft.Empty);
                }
                var continuationEnd = ContinuationEnd(target);
                return new StructuredExceptionContinuationDraft(
                    target,
                    internalLoop is null
                        ? MarkContinuationOwnership(_ranges.Structure(state,
                            target,
                            continuationEnd - target))
                        : _loopContinuations.Build(state,
                            target,
                            continuationEnd - target,
                            internalLoop));
            }).ToImmutableArray();
            // A top-level block's continuations after the first are emitted after it, each behind a test of the leave
            // that chose it, when none of them can run on into the next: straight-line blocks ending in a return, or a
            // closed set of blocks that ends in a return or a throw on every path (TerminalContinuationFinder), whose
            // blocks the continuation then owns as it owns straight-line ones. Otherwise a dispatcher takes them all.
            var dispatchable = !parents.ContainsKey(source) &&
                continuations.Length > 1 &&
                leaveTargets.All(target => FindInternalLoop(source, target) is null);
            var open = dispatchable
                ? continuations.Skip(1).Where(continuation => !IsReturnContinuation(continuation.Body)).ToArray()
                : [];
            var closed = open.Select(continuation => _terminalContinuations.Find(state.Graph,
                    continuation.TargetOffset,
                    ContinuationEnd(continuation.TargetOffset),
                    [.. continuations.Select(other => other.TargetOffset)
                        .Where(other => other != continuation.TargetOffset)]))
                .ToArray();
            var canDispatchContinuations = closed.Any(blocks => blocks is null);
            if (!canDispatchContinuations)
            {
                foreach (var blocks in closed) state.ContinuationOwnedBlocks.UnionWith(blocks!);
            }

            // Where one continuation's code ends (ContinuationExtentFinder), found once per target.
            int ContinuationEnd(int target)
            {
                if (!continuationEnds.TryGetValue(target, out var end))
                {
                    continuationEnds[target] = end =
                        _continuationExtents.End(state.Graph, [target], continuationBoundary, methodEnd);
                }
                return end;
            }

            // A dispatcher takes the code of all the continuations, which ends at the boundary, where it joins the code
            // after it, only if none of them runs past the boundary.
            var dispatcherEnd = canDispatchContinuations
                ? _continuationExtents.End(state.Graph,
                    [.. continuations.Select(continuation => continuation.TargetOffset)],
                    continuationBoundary,
                    methodEnd)
                : methodEnd;
            var dispatcher = canDispatchContinuations
                ? _continuationDispatchers.Build(state,
                    continuations.Select(continuation => continuation.TargetOffset),
                    dispatcherEnd)
                : null;
            return new StructuredExceptionGroupDraft(
                source.TryOffset,
                source.TryLength,
                parts.ToImmutable(),
                [
                    ..source.Regions.Select((region, clauseIndex) =>
                        new StructuredExceptionClauseDraft(
                            region,
                            BuildNestedExceptionRange(
                                region.HandlerOffset,
                                region.HandlerLength,
                                ExceptionScopeKind.Handler,
                                clauseIndex),
                            region.FilterOffset is int filterOffset
                                ? BuildNestedExceptionRange(
                                    filterOffset,
                                    region.HandlerOffset - filterOffset,
                                    ExceptionScopeKind.Filter,
                                    clauseIndex)
                                : null))
                ],
                dispatcher is null
                    ? continuations
                    : [.. continuations.Select(continuation =>
                        continuation with { Body = StructuredSequenceDraft.Empty })])
            {
                ContinuationDispatcher = dispatcher,
                ContinuationJoinBlock = dispatcher is null || dispatcherEnd >= methodEnd
                    ? null
                    : state.Graph.GetBlockAtOffset(continuationBoundary).Index,
            };

            // The next child in the same scope (children are in try order), or the scope's end.
            int FindChildContinuationBoundary(ExceptionGroupSource child)
            {
                var scope = childScopes[child];
                var starts = siblingStarts[scope];
                var low = 0;
                var high = starts.Count;
                while (low < high)
                {
                    var middle = low + (high - low) / 2;
                    if (starts[middle] <= child.TryOffset) low = middle + 1;
                    else high = middle;
                }
                return low < starts.Count ? starts[low] : scope.End;
            }

            StructuredSequenceDraft BuildNestedExceptionRange(
                int offset,
                int length,
                ExceptionScopeKind kind,
                int clauseIndex)
            {
                var nestedChildren = children
                    .Where(child => childScopes[child].Kind == kind &&
                        childScopes[child].ClauseIndex == clauseIndex)
                    .ToArray();
                foreach (var child in nestedChildren)
                {
                    state.ExceptionGroupsByEntry.Add(
                        state.Graph.GetBlockAtOffset(child.TryOffset).Index,
                        builtChildren[child]);
                }
                try
                {
                    return _ranges.Structure(state, offset, length);
                }
                finally
                {
                    foreach (var child in nestedChildren)
                    {
                        state.ExceptionGroupsByEntry.Remove(
                            state.Graph.GetBlockAtOffset(child.TryOffset).Index);
                    }
                }
            }
        }

        var topLevel = sources
            .Where(source => !parents.ContainsKey(source))
            .OrderBy(source => source.TryOffset)
            .ToArray();
        var builtTopLevel = new StructuredExceptionGroupDraft[topLevel.Length];
        for (var index = topLevel.Length - 1; index >= 0; index--)
        {
            var built = Build(
                topLevel[index],
                index + 1 < topLevel.Length
                    ? topLevel[index + 1].TryOffset
                    : methodEnd);
            builtTopLevel[index] = built;
            state.ExceptionGroupsByEntry[state.Graph.GetBlockAtOffset(built.TryOffset).Index] = built;
        }
        return [.. builtTopLevel];

        StructuredSequenceDraft MarkContinuationOwnership(
            StructuredSequenceDraft sequence) => IsReturnContinuation(sequence)
                ? new StructuredSequenceDraft([.. sequence.Regions
                    .Cast<StructuredBlockDraft>()
                    .Select(block => block with
                    {
                        IsOriginal = MarkBlockContinuationOwnership(block),
                    })])
                : sequence;

        bool MarkBlockContinuationOwnership(StructuredBlockDraft block)
        {
            state.ContinuationOwnedBlocks.Add(block.Block.Index);
            return block.IsOriginal;
        }

        static bool IsReturnContinuation(StructuredSequenceDraft sequence)
        {
            var blocks = sequence.Regions.OfType<StructuredBlockDraft>().ToArray();
            return blocks.Length == sequence.Regions.Length &&
                blocks.TakeLast(1)
                    .Select(block => block.Block.Terminator.Operation)
                    .SequenceEqual([CilOperation.Return]);
        }

        LoopRegion? FindInternalLoop(ExceptionGroupSource source, int target)
        {
            var tryEntry = state.Graph.GetBlockAtOffset(source.TryOffset).Index;
            var targetBlock = state.Graph.GetBlockAtOffset(target).Index;
            // A continuation that stays inside the parent's protected code is
            // structured inside that parent, so it cannot use the labels of a loop
            // that encloses the parent: reaching them means leaving the parent.
            var enclosingParent = parents.TryGetValue(source, out var parentSource) &&
                _exceptionScopes.Find(parentSource, source)!.Value.Kind ==
                    ExceptionScopeKind.Protected &&
                IsInProtectedCode(parentSource, targetBlock)
                    ? parentSource
                    : null;
            return state.LoopsByHeader.Values
                .Where(loop =>
                    // The condition-exit extension executes after leaving the
                    // loop. Only protected regions in the body can route their
                    // continuations through this loop's break/continue labels.
                    loop.BodyComponent.Contains(tryEntry) &&
                    HoldsGroup(loop, tryEntry) &&
                    (loop.Component.Contains(targetBlock) ||
                        loop.Exit == targetBlock) &&
                    (enclosingParent is null ||
                        IsInProtectedCode(enclosingParent, loop.Header)))
                .OrderBy(loop => loop.Component.Count)
                .FirstOrDefault();
        }

        // A loop whose header is the try's entry lies inside the protected code, as a while (true) loop that is
        // the first statement of a using block does: its back edges come from inside the try. A branch from outside
        // to the try's first instruction, and a catch's leave into its own try, go to an entry point the try reentry
        // lowerer puts just before the try, so a loop around a try has its header there or earlier (Roslyn's starts
        // at a nop before the try). A loop inside the group breaks out of itself, not out of the group, so the
        // group's continuations cannot use its labels, even where the loop's exit is a continuation's target. Any
        // other header whose loop holds the try's entry is outside the protected code.
        static bool HoldsGroup(LoopRegion loop, int tryEntry) => loop.Header != tryEntry;

        bool IsInProtectedCode(ExceptionGroupSource group, int block)
        {
            var offset = state.Graph.GetBlock(block).StartOffset;
            return offset >= group.TryOffset && offset < group.TryEnd;
        }
    }

    private static bool HoldsReachableBlock(int[] starts, int start, int end)
    {
        var low = 0;
        var high = starts.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (starts[middle] < start) low = middle + 1;
            else high = middle;
        }
        return low < starts.Length && starts[low] < end;
    }

    // Each group's parent: of the groups with a scope (its try, a filter, a handler) holding the group from its try to
    // the end of its last handler, the one whose scope is shortest, the earlier group on a tie. One sweep over the
    // groups in try order with the scopes open there, which are nested in valid IL: the groups' nesting depth per
    // group, not every group per group.
    private static Dictionary<ExceptionGroupSource, ExceptionGroupSource> Parents(ExceptionGroupSource[] sources)
    {
        var scopes = new List<(int Start, int End, int Owner)>();
        for (var owner = 0; owner < sources.Length; owner++)
        {
            var source = sources[owner];
            scopes.Add((source.TryOffset, source.TryEnd, owner));
            foreach (var region in source.Regions)
            {
                if (region.FilterOffset is int filter) scopes.Add((filter, region.HandlerOffset, owner));
                scopes.Add((region.HandlerOffset, checked(region.HandlerOffset + region.HandlerLength), owner));
            }
        }
        var byStart = scopes.OrderBy(scope => scope.Start).ToArray();
        var parents = new Dictionary<ExceptionGroupSource, ExceptionGroupSource>();
        var open = new List<(int Start, int End, int Owner)>();
        var next = 0;
        foreach (var child in Enumerable.Range(0, sources.Length).OrderBy(index => sources[index].TryOffset))
        {
            var start = sources[child].TryOffset;
            var extent = sources[child].Extent;
            while (next < byStart.Length && byStart[next].Start <= start) open.Add(byStart[next++]);
            open.RemoveAll(scope => scope.End < start);
            var best = -1;
            var bestLength = 0;
            foreach (var scope in open)
            {
                if (scope.Owner == child || scope.End < extent) continue;
                var length = scope.End - scope.Start;
                if (best < 0 || length < bestLength || length == bestLength && scope.Owner < best)
                {
                    best = scope.Owner;
                    bestLength = length;
                }
            }
            if (best >= 0) parents.Add(sources[child], sources[best]);
        }
        return parents;
    }
}
