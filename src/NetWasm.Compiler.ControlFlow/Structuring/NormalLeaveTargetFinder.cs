// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// The targets of the leaves that take control out of a group normally. Only the blocks inside the group's own try and
// handlers are read, found by a binary search over the method's block starts (sorted once per graph), and a leave's
// test against the group's nested groups is a binary search over their merged spans: the cost is the group's own
// code, not the method's for every group.
internal sealed class NormalLeaveTargetFinder : INormalLeaveTargetFinder
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ControlFlowGraph, BasicBlock[]> s_byStart =
        new();

    public int[] Find(ControlFlowStructuringState state,
        IEnumerable<CilExceptionRegion> regions,
        IReadOnlyCollection<ExceptionGroupSource> excluded,
        IReadOnlyCollection<ExceptionGroupSource> handlerExcluded)
    {
        var array = regions.ToArray();
        var tryOffset = array[0].TryOffset;
        var tryEnd = checked(tryOffset + array[0].TryLength);
        (int Start, int End)[] ranges = [.. array
            .Select(region => (
                region.HandlerOffset,
                checked(region.HandlerOffset + region.HandlerLength)))
            .Prepend((tryOffset, tryEnd))];
        var children = Merge(excluded);
        var handlerChildren = Merge(handlerExcluded);
        var blocks = s_byStart.GetValue(state.Graph, graph => [.. graph.Blocks.OrderBy(block => block.StartOffset)]);
        var visited = new HashSet<int>();
        var targets = new HashSet<int>();
        foreach (var (start, end) in ranges)
        {
            for (var index = FirstAtOrAfter(blocks, start); index < blocks.Length && blocks[index].StartOffset < end; index++)
            {
                var block = blocks[index];
                if (!state.Graph.ReachableBlocks.Contains(block.Index) || !visited.Add(block.Index)) continue;
                foreach (var instruction in block.Instructions)
                {
                    if (instruction.Operation != CilOperation.Leave) continue;
                    var target = ((CilOperand.BranchTarget)instruction.Operand).Offset;
                    // A leave from a group inside a handler stays that group's own unless it
                    // leaves the handler too, as a continue through a finally in a loop's catch
                    // handler does: then this region continues there as well, so the nested
                    // group's continuation routes through this one.
                    if (Holds(handlerChildren, block.StartOffset))
                    {
                        if (!ranges.Any(range => target >= range.Start && target < range.End)) targets.Add(target);
                        continue;
                    }
                    // A child's leave stays inside this region only when its target
                    // does; a backward leave to a loop header before the region
                    // leaves it too.
                    if (!(Holds(children, block.StartOffset) && target >= tryOffset && target < tryEnd)) targets.Add(target);
                }
            }
        }
        return [.. targets.Order()];
    }

    // The groups' spans, each from its try to the end of its last handler, merged and in order.
    private static (int Start, int End)[] Merge(IReadOnlyCollection<ExceptionGroupSource> groups)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var (start, end) in groups.Select(group => (group.TryOffset, group.Extent)).OrderBy(span => span.TryOffset))
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            }
            else
            {
                merged.Add((start, end));
            }
        }
        return [.. merged];
    }

    // Whether one of the merged spans holds the offset.
    private static bool Holds((int Start, int End)[] spans, int offset)
    {
        var low = 0;
        var high = spans.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (spans[middle].Start <= offset) low = middle + 1;
            else high = middle;
        }
        return low > 0 && offset < spans[low - 1].End;
    }

    private static int FirstAtOrAfter(BasicBlock[] blocks, int offset)
    {
        var low = 0;
        var high = blocks.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (blocks[middle].StartOffset < offset) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}
