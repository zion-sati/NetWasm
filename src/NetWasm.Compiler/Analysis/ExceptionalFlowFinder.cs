// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IExceptionalFlowFinder
{
    ExceptionalFlow Find(ControlFlowGraph graph);
}

// What a dataflow over a method's normal edges does not see of its protected blocks. Entries: each handler and filter
// begins where control arrives from wherever its protected block may throw (or, for a filter's handler, from the
// filter), with no normal edge in. FinallyLeaves: a leave out of a finally runs the finally before it reaches its
// target, while the graph's edge goes straight to the target. FilterBlocks: a filter runs as a funclet of its own, with
// locals of its own. A forward analysis of facts that hold on every path starts each entry, and arrives over each such
// edge, knowing nothing, and keeps a filter's code to itself.
internal sealed record ExceptionalFlow(
    ImmutableHashSet<int> Entries,
    ImmutableHashSet<(int From, int To)> FinallyLeaves,
    ImmutableHashSet<int> FilterBlocks)
{
    public static ExceptionalFlow None { get; } = new([], [], []);
}

internal sealed class ExceptionalFlowFinder : IExceptionalFlowFinder
{
    public ExceptionalFlow Find(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var body = graph.MethodBody;
        var regions = body.ExceptionRegions;
        if (regions.IsEmpty) return ExceptionalFlow.None;

        var blockStarts = graph.Blocks.Select(block => block.StartOffset).ToArray();
        var entries = ImmutableHashSet.CreateBuilder<int>();
        var filterBlocks = ImmutableHashSet.CreateBuilder<int>();
        foreach (var region in regions)
        {
            entries.Add(graph.GetBlockAtOffset(region.HandlerOffset).Index);
            if (region.FilterOffset is not int filter) continue;
            entries.Add(graph.GetBlockAtOffset(filter).Index);
            // The blocks from the filter's start up to its handler's, in offset order.
            var first = Array.BinarySearch(blockStarts, filter);
            for (var at = first < 0 ? ~first : first; at < blockStarts.Length && blockStarts[at] < region.HandlerOffset; at++)
            {
                filterBlocks.Add(graph.GetBlockAtOffset(blockStarts[at]).Index);
            }
        }

        var nesting = new ExceptionRegionNesting(body.Instructions, regions);
        var finallyLeaves = ImmutableHashSet.CreateBuilder<(int From, int To)>();
        foreach (var block in graph.Blocks)
        {
            if (block.Instructions.IsEmpty || block.Terminator is not
                { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget target } leave)
            {
                continue;
            }
            if (nesting.TriesLeft(leave.Offset, target.Offset).Any(region => regions[region].Kind == CilExceptionRegionKind.Finally))
            {
                finallyLeaves.Add((block.Index, graph.GetBlockAtOffset(target.Offset).Index));
            }
        }
        return new ExceptionalFlow(entries.ToImmutable(), finallyLeaves.ToImmutable(), filterBlocks.ToImmutable());
    }
}
