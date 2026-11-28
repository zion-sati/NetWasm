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
    // The protected blocks around each origin come from one sweep over the origins in offset order, and each cleanup
    // handler's innermost endfinally blocks and each filter's blocks are found once: the cost is the method's blocks and
    // regions times their nesting, not every region and block for every leave and every throwing block.
    public StackStorageContinuations Analyze(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var graph = method.ControlFlow.Graph;
        var regions = method.Body.ExceptionRegions;
        var reachable = graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index)).ToArray();
        var normal = graph.Blocks.ToDictionary(block => block.Index,
            block => graph.Successors[block.Index].Select(index => new StackStorageFlowEdge(index, false)).ToHashSet());
        var exceptional = graph.Blocks.ToDictionary(block => block.Index, _ => new HashSet<StackStorageFlowEdge>());

        // Every offset a route starts from: leave instructions and the starts of blocks that may throw.
        var origins = new HashSet<int>();
        foreach (var block in reachable)
        {
            if (block.Terminator.Operation == CilOperation.Leave) origins.Add(block.Terminator.Offset);
            origins.Add(block.StartOffset);
        }
        var chains = TryChains(regions, origins);
        var innermostEnds = InnermostCleanupEnds(regions, reachable);
        var filterBlocks = FilterBlocks(regions, reachable);
        var inFilter = InAnyFilter(regions, reachable);

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
                inFilter.Contains(block.Index))
            {
                continue;
            }
            var candidates = chains[block.StartOffset]
                .Where(index => regions[index].Kind is CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter)
                .ToArray();
            var suffix = Route(block.StartOffset, null, exceptionalExit: true);
            for (var position = candidates.Length - 1; position >= 0; position--)
            {
                var candidate = regions[candidates[position]];
                var accepted = Route(block.StartOffset, candidate.HandlerOffset, exceptionalExit: true);
                if (candidate.Kind == CilExceptionRegionKind.Catch)
                {
                    // Matching is deliberately a may-flow choice: a catch may
                    // match or search may continue to a later clause.
                    suffix.UnionWith(accepted);
                    continue;
                }
                foreach (var filterBlock in filterBlocks[candidates[position]])
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
            var cleanup = chains[origin]
                .Where(index =>
                {
                    var region = regions[index];
                    // A finally/fault cannot share its try range with catch
                    // or filter clauses. Nested handlers are inside this try.
                    return (region.Kind == CilExceptionRegionKind.Finally ||
                            exceptionalExit && region.Kind == CilExceptionRegionKind.Fault) &&
                        (target is null || !InTry(region, target.Value));
                })
                .ToArray();
            var next = target is { } destination ? new HashSet<StackStorageFlowEdge> { Edge(destination) } : [];
            for (var position = cleanup.Length - 1; position >= 0; position--)
            {
                foreach (var end in innermostEnds[cleanup[position]]) normal[end.Index].UnionWith(next);
                next = [Edge(regions[cleanup[position]].HandlerOffset)];
            }
            return next;
        }
    }

    // For each origin, the regions whose try holds it, innermost first (shortest try, then the later start, then the
    // order the regions are declared in): one sweep over the origins with the tries open there.
    private static Dictionary<int, int[]> TryChains(ImmutableArray<CilExceptionRegion> regions, HashSet<int> origins)
    {
        var byStart = Enumerable.Range(0, regions.Length).OrderBy(index => regions[index].TryOffset).ToArray();
        var open = new List<int>();
        var next = 0;
        var chains = new Dictionary<int, int[]>(origins.Count);
        foreach (var origin in origins.Order())
        {
            while (next < byStart.Length && regions[byStart[next]].TryOffset <= origin) open.Add(byStart[next++]);
            open.RemoveAll(index => !InTry(regions[index], origin) && regions[index].TryOffset + regions[index].TryLength <= origin);
            chains[origin] =
            [
                .. open.Where(index => InTry(regions[index], origin))
                    .OrderBy(index => regions[index].TryLength)
                    .ThenByDescending(index => regions[index].TryOffset)
                    .ThenBy(index => index),
            ];
        }
        return chains;
    }

    // For each cleanup region, the reachable blocks ending in endfinally whose endfinally lies in its handler and in no
    // shorter cleanup handler: the innermost cleanup handler around each endfinally, found by one sweep.
    private static List<BasicBlock>[] InnermostCleanupEnds(ImmutableArray<CilExceptionRegion> regions, BasicBlock[] reachable)
    {
        var ends = new List<BasicBlock>[regions.Length];
        for (var index = 0; index < regions.Length; index++) ends[index] = [];
        var cleanups = Enumerable.Range(0, regions.Length)
            .Where(index => regions[index].Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault)
            .OrderBy(index => regions[index].HandlerOffset)
            .ToArray();
        var open = new List<int>();
        var next = 0;
        foreach (var block in reachable.Where(block => block.Terminator.Operation == CilOperation.EndFinally)
                     .OrderBy(block => block.Terminator.Offset))
        {
            var offset = block.Terminator.Offset;
            while (next < cleanups.Length && regions[cleanups[next]].HandlerOffset <= offset) open.Add(cleanups[next++]);
            open.RemoveAll(index => regions[index].HandlerOffset + regions[index].HandlerLength <= offset);
            var holding = open.Where(index => InHandler(regions[index], offset)).ToArray();
            if (holding.Length == 0) continue;
            var shortest = holding.Min(index => regions[index].HandlerLength);
            foreach (var index in holding)
            {
                if (regions[index].HandlerLength == shortest) ends[index].Add(block);
            }
        }
        return ends;
    }

    // For each region with a filter, the reachable blocks starting in the filter.
    private static List<BasicBlock>[] FilterBlocks(ImmutableArray<CilExceptionRegion> regions, BasicBlock[] reachable)
    {
        var blocks = new List<BasicBlock>[regions.Length];
        var byStart = reachable.OrderBy(block => block.StartOffset).ToArray();
        for (var index = 0; index < regions.Length; index++)
        {
            blocks[index] = [];
            if (regions[index].FilterOffset is not { } filter) continue;
            var low = 0;
            var high = byStart.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (byStart[middle].StartOffset < filter) low = middle + 1;
                else high = middle;
            }
            for (var position = low; position < byStart.Length && byStart[position].StartOffset < regions[index].HandlerOffset;
                 position++)
            {
                blocks[index].Add(byStart[position]);
            }
        }
        // In the order the blocks are reachable, as the scan over them gave them.
        var order = reachable.Select((block, position) => (block.Index, position)).ToDictionary(pair => pair.Index, pair => pair.position);
        foreach (var list in blocks) list.Sort((left, right) => order[left.Index].CompareTo(order[right.Index]));
        return blocks;
    }

    // The reachable blocks that start inside some filter.
    private static HashSet<int> InAnyFilter(ImmutableArray<CilExceptionRegion> regions, BasicBlock[] reachable)
    {
        var inside = new HashSet<int>();
        foreach (var list in FilterBlocks(regions, reachable))
        {
            foreach (var block in list) inside.Add(block.Index);
        }
        return inside;
    }

    private static bool InTry(CilExceptionRegion region, int offset) =>
        offset >= region.TryOffset && offset < region.TryOffset + region.TryLength;

    private static bool InHandler(CilExceptionRegion region, int offset) =>
        offset >= region.HandlerOffset && offset < region.HandlerOffset + region.HandlerLength;


    private static ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> Freeze(
        Dictionary<int, HashSet<StackStorageFlowEdge>> edges) => edges.ToImmutableDictionary(pair => pair.Key,
        pair => pair.Value.OrderBy(edge => edge.Block).ThenBy(edge => edge.ResetStack).ToImmutableArray());
}
