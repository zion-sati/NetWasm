// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Strategy: derive analysis-only semantic EH continuations. Filters search
// before cleanup unwinds; a shared cleanup unions its registered destinations.
public sealed class StackStorageContinuationAnalyzer : IStackStorageContinuationAnalyzer
{
    public StackStorageContinuations Analyze(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var graph = method.ControlFlow.Graph;
        var regions = method.Body.ExceptionRegions;
        var reachable = graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)).ToArray();
        var normal = graph.Blocks.ToDictionary(block => block.Index,
            block => graph.Successors[block.Index].Select(index => new StackStorageFlowEdge(index, false)).ToHashSet());
        var exceptional = graph.Blocks.ToDictionary(block => block.Index, _ => new HashSet<StackStorageFlowEdge>());
        foreach (var block in reachable)
        {
            if (block.Terminator.Operation == CilOperation.Leave)
            {
                normal[block.Index].Clear();
                var target = ((CilOperand.BranchTarget)block.Terminator.Operand).Offset;
                normal[block.Index].UnionWith(Route(block.Terminator.Offset, target, exceptionalExit: false));
            }
        }
        foreach (var block in reachable)
        {
            if (!block.Instructions.Any(CilSafepointClassifier.MayTransferControlExceptionally) ||
                regions.Any(region => InFilter(region, block.StartOffset)))
            {
                continue;
            }
            var candidates = regions.Select((region, order) => (region, order))
                .Where(item => InTry(item.region, block.StartOffset) &&
                    item.region.Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter)
                .OrderBy(item => item.region.TryLength).ThenByDescending(item => item.region.TryOffset)
                .ThenBy(item => item.order).Select(item => item.region).ToArray();
            var suffix = Route(block.StartOffset, null, exceptionalExit: true);
            for (var index = candidates.Length - 1; index >= 0; index--)
            {
                var candidate = candidates[index];
                var accepted = Route(block.StartOffset, candidate.HandlerOffset, exceptionalExit: true);
                if (candidate.Kind == CilExceptionRegionKind.Catch)
                {
                    // Matching is deliberately a may-flow choice: a catch may
                    // match or search may continue to a later clause.
                    suffix.UnionWith(accepted);
                    continue;
                }
                foreach (var filterBlock in reachable.Where(item => InFilter(candidate, item.StartOffset)))
                {
                    // ECMA-335 I.12.4.2.7 forbids EH entries nested inside a
                    // filter, so its endfilter always belongs to this clause.
                    if (filterBlock.Terminator.Operation == CilOperation.EndFilter)
                    {
                        normal[filterBlock.Index].UnionWith(accepted);
                        normal[filterBlock.Index].UnionWith(suffix);
                    }
                    if (filterBlock.Instructions.Any(CilSafepointClassifier.MayTransferControlExceptionally))
                    {
                        // A throwing filter is rejected, not accepted. Use the
                        // original throw's cleanup scope, not the filter offset.
                        exceptional[filterBlock.Index].UnionWith(suffix);
                    }
                }
                suffix = [Edge(candidate.FilterOffset!.Value)];
            }
            exceptional[block.Index].UnionWith(suffix);
        }
        return new(Freeze(normal), Freeze(exceptional));

        StackStorageFlowEdge Edge(int offset) => new(graph.GetBlockAtOffset(offset).Index, true);

        HashSet<StackStorageFlowEdge> Route(int origin, int? target, bool exceptionalExit)
        {
            var cleanup = regions.Where(region =>
                    (region.Kind == CilExceptionRegionKind.Finally ||
                        exceptionalExit && region.Kind == CilExceptionRegionKind.Fault) &&
                    // A finally/fault cannot share its try range with catch
                    // or filter clauses. Nested handlers are inside this try.
                    InTry(region, origin) &&
                    (target is null || !InTry(region, target.Value)))
                .OrderBy(region => region.TryLength).ThenByDescending(region => region.TryOffset).ToArray();
            var next = target is { } destination ? new HashSet<StackStorageFlowEdge> { Edge(destination) } : [];
            for (var index = cleanup.Length - 1; index >= 0; index--)
            {
                var region = cleanup[index];
                foreach (var end in reachable.Where(block => block.Terminator.Operation == CilOperation.EndFinally &&
                             InHandler(region, block.Terminator.Offset) && IsInnermostCleanup(region, block.Terminator.Offset)))
                {
                    normal[end.Index].UnionWith(next);
                }
                next = [Edge(region.HandlerOffset)];
            }
            return next;
        }

        bool IsInnermostCleanup(CilExceptionRegion region, int offset) => !regions.Any(inner =>
            inner.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault &&
            inner.HandlerLength < region.HandlerLength && InHandler(inner, offset));
    }

    private static bool InTry(CilExceptionRegion region, int offset) =>
        offset >= region.TryOffset && offset < region.TryOffset + region.TryLength;

    private static bool InHandler(CilExceptionRegion region, int offset) =>
        offset >= region.HandlerOffset && offset < region.HandlerOffset + region.HandlerLength;

    private static bool InFilter(CilExceptionRegion region, int offset) =>
        region.FilterOffset is { } start && offset >= start && offset < region.HandlerOffset;

    private static ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> Freeze(
        Dictionary<int, HashSet<StackStorageFlowEdge>> edges) => edges.ToImmutableDictionary(pair => pair.Key,
        pair => pair.Value.OrderBy(edge => edge.Block).ThenBy(edge => edge.ResetStack).ToImmutableArray());
}
