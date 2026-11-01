using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class UnreachableInstructionRewriter(IControlFlowGraphBuilder graphs) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var graph = _graphs.Build(body);
        var dead = graph.Blocks
            .Where(block => !graph.ReachableBlocks.Contains(block.Index))
            .SelectMany(block => block.Instructions)
            .Where(instruction => instruction.Operation is not
                (CilOperation.Nop or CilOperation.Return or CilOperation.Throw or
                 CilOperation.Rethrow or CilOperation.EndFinally or CilOperation.EndFilter) &&
                instruction.Operand is not CilOperand.BranchTarget)
            .Select(instruction => instruction.Offset)
            .ToHashSet();
        return dead.Count == 0 ? body : body with
        {
            Instructions = [.. body.Instructions.Select(instruction => dead.Contains(instruction.Offset)
                ? instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() }
                : instruction)],
        };
    }
}
