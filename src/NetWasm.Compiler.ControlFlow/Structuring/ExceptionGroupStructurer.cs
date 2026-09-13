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
    INormalLeaveTargetFinder leaveTargets) : IExceptionGroupStructurer
{
    private readonly IExceptionScopeFinder _exceptionScopes = exceptionScopes ?? throw new ArgumentNullException(nameof(exceptionScopes));
    private readonly IContinuationDispatcherBuilder _continuationDispatchers = continuationDispatchers ?? throw new ArgumentNullException(nameof(continuationDispatchers));
    private readonly ILoopContinuationBuilder _loopContinuations = loopContinuations ?? throw new ArgumentNullException(nameof(loopContinuations));
    private readonly INestedFlowClassifier _nestedFlows = nestedFlows ?? throw new ArgumentNullException(nameof(nestedFlows));
    private readonly IBlockRangeStructurer _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
    private readonly INormalLeaveTargetFinder _leaveTargets = leaveTargets ?? throw new ArgumentNullException(nameof(leaveTargets));

    public ImmutableArray<StructuredExceptionGroupDraft> Structure(ControlFlowStructuringState state,
        ImmutableArray<CilExceptionRegion> regions)
    {
        if (regions.IsEmpty)
        {
            return [];
        }
        var sources = regions
            .GroupBy(region => (region.TryOffset, region.TryLength))
            .Select(group => new ExceptionGroupSource(
                group.Key.TryOffset,
                group.Key.TryLength,
                [.. group]))
            .ToArray();
        var parents = new Dictionary<ExceptionGroupSource, ExceptionGroupSource>();
        foreach (var child in sources)
        {
            var parent = sources
                .Where(candidate => candidate != child &&
                    _exceptionScopes.Find(candidate, child) is not null)
                .OrderBy(candidate =>
                    _exceptionScopes.Find(candidate, child)!.Value.Length)
                .FirstOrDefault();
            if (parent is not null)
            {
                parents.Add(child, parent);
            }
        }
        var methodEnd = state.Graph.MethodBody.Instructions[^1].NextOffset;

        StructuredExceptionGroupDraft Build(
            ExceptionGroupSource source,
            int continuationBoundary)
        {
            var children = sources
                .Where(candidate => parents.GetValueOrDefault(candidate) == source)
                .OrderBy(candidate => candidate.TryOffset)
                .ToArray();
            var childScopes = children.ToDictionary(
                child => child,
                child => _exceptionScopes.Find(source, child)!.Value);
            var protectedChildren = children
                .Where(child => childScopes[child].Kind ==
                    ExceptionScopeKind.Protected)
                .ToArray();
            var nestedSources = sources
                .Where(candidate => candidate != source &&
                    IsContained(source, candidate))
                .ToArray();
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
            var continuations = leaveTargets.Select((target, index) =>
            {
                var internalLoop = FindInternalLoop(source, target);
                if (index == 0 &&
                    internalLoop is not null &&
                    !nestedProtectedPart)
                {
                    return new StructuredExceptionContinuationDraft(
                        target,
                        StructuredSequenceDraft.Empty);
                }
                var continuationEnd = continuationBoundary +
                    (methodEnd - continuationBoundary) *
                    Math.Max(0, Math.Sign((long)target - continuationBoundary));
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
            var canDispatchContinuations = !parents.ContainsKey(source) &&
                continuations.Length > 1 &&
                continuations.Skip(1).Any(continuation =>
                    !IsReturnContinuation(continuation.Body)) &&
                leaveTargets.All(target => FindInternalLoop(source, target) is null);
            var dispatcher = canDispatchContinuations
                ? _continuationDispatchers.Build(state,
                    continuations.Select(continuation => continuation.TargetOffset),
                    continuationBoundary,
                    methodEnd)
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
                ContinuationJoinBlock = dispatcher is null ||
                    continuationBoundary >= methodEnd ||
                    leaveTargets.Any(target => target > continuationBoundary)
                        ? null
                        : state.Graph.GetBlockAtOffset(continuationBoundary).Index,
            };

            int FindChildContinuationBoundary(ExceptionGroupSource child)
            {
                var scope = childScopes[child];
                return children
                    .Where(candidate => candidate != child &&
                        childScopes[candidate] == scope &&
                        candidate.TryOffset > child.TryOffset)
                    .Select(candidate => candidate.TryOffset)
                    .DefaultIfEmpty(scope.End)
                    .Min();
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
            return state.LoopsByHeader.Values
                .Where(loop =>
                    loop.Component.Contains(tryEntry) &&
                    (loop.Component.Contains(targetBlock) ||
                        loop.Exit == targetBlock))
                .OrderBy(loop => loop.Component.Count)
                .FirstOrDefault();
        }
    }

    private bool IsContained(ExceptionGroupSource container, ExceptionGroupSource candidate) =>
        _exceptionScopes.Find(container, candidate) is not null;
}
