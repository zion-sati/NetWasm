// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopContinueTargetFinder(
    ICommonReachableBlockFinder joins,
    IReachableBlockFinder reachableBlocks) : ILoopContinueTargetFinder
{
    private readonly ICommonReachableBlockFinder _joins = joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));

    public int Find(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        int header)
    {
        var leaveTargets = component
            .Select(graph.GetBlock)
            .Where(block => block.Terminator.Operation == CilOperation.Leave)
            .Select(block => ((CilOperand.BranchTarget)block.Terminator.Operand).Offset)
            .Select(offset => graph.GetBlockAtOffset(offset).Index)
            .Distinct()
            .ToArray();
        var internalTargets = leaveTargets
            .Where(component.Contains)
            .ToArray();
        if (leaveTargets.Length <= 1 || internalTargets.Length == 0)
        {
            return header;
        }
        var candidate = _joins.Find(state,
            internalTargets,
            header,
            component).GetValueOrDefault(header);
        if (candidate == header)
        {
            return header;
        }

        // A shared exception-leave destination need not be a shared latch for
        // ordinary back edges. A bypass would re-enter the loop header while
        // structuring its body, instead of reaching the selected continue point.
        var stops = ImmutableHashSet.Create(candidate);
        return state.ContinuationGraph.Successors[header]
            .Where(component.Contains)
            .Any(start => _reachableBlocks.Find(state.ContinuationGraph, start, stops, component)
                .Contains(header))
            ? header
            : candidate;
    }
}
