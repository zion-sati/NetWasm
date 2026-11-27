// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionAwareDispatcherBlockSelector : IExceptionAwareDispatcherBlockSelector
{
    public ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(allowed);

        // The blocks from an exception group's try to the end of its last handler, for the groups the component
        // enters, as sorted ranges that neither overlap nor touch: whether a block lies in one is a binary search,
        // where testing each block against each group made a dispatcher over N groups cost N squared.
        var exceptionGroups = component.Count < state.ExceptionGroupsByEntry.Count
            ? component.Where(state.ExceptionGroupsByEntry.ContainsKey).Select(block => state.ExceptionGroupsByEntry[block])
            : state.ExceptionGroupsByEntry.Where(entry => component.Contains(entry.Key)).Select(entry => entry.Value);
        var extents = exceptionGroups
            .Select(group => (Start: group.TryOffset, End: group.Clauses.Max(clause => checked(
                clause.Region.HandlerOffset + clause.Region.HandlerLength))))
            .Where(extent => extent.Start < extent.End)
            .OrderBy(extent => extent.Start)
            .ToList();
        var ranges = new List<(int Start, int End)>(extents.Count);
        foreach (var extent in extents)
        {
            if (ranges.Count > 0 && extent.Start <= ranges[^1].End)
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, extent.End));
            else
                ranges.Add(extent);
        }
        var excluded = allowed
            .Where(block => InAny(ranges, state.Graph.GetBlock(block).StartOffset))
            .ToImmutableHashSet();
        var owned = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Stack<int>(component.Where(block => !excluded.Contains(block)));
        while (pending.TryPop(out var block))
        {
            if (!component.Contains(block) || excluded.Contains(block) || !owned.Add(block))
            {
                continue;
            }

            foreach (var successor in state.Graph.Successors[block])
            {
                pending.Push(successor);
            }
        }

        return owned.ToImmutable();
    }

    private static bool InAny(List<(int Start, int End)> ranges, int offset)
    {
        var low = 0;
        var high = ranges.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (offset < ranges[middle].Start) high = middle - 1;
            else if (offset >= ranges[middle].End) low = middle + 1;
            else return true;
        }
        return false;
    }
}
