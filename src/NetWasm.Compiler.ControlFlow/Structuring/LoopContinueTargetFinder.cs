using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopContinueTargetFinder(ICommonReachableBlockFinder joins) : ILoopContinueTargetFinder
{
    private readonly ICommonReachableBlockFinder _joins = joins ?? throw new ArgumentNullException(nameof(joins));

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
        return _joins.Find(state,
            internalTargets,
            header,
            component).GetValueOrDefault(header);
    }
}
