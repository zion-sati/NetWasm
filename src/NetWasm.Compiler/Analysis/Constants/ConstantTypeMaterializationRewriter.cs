using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// typeof loads a type token and materializes it. Within one basic block nothing
// else reaches the materialization, so it can name the token's type; a block
// boundary between them, as in c ? typeof(A) : typeof(B), leaves it dynamic.
internal sealed class ConstantTypeMaterializationRewriter(IControlFlowGraphBuilder graphs) : IMethodRewriteRule
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(static instruction => instruction is
            { Operation: CilOperation.MaterializeType, Operand: CilOperand.None }))
        {
            return body;
        }

        var rewritten = body.Instructions.ToDictionary(instruction => instruction.Offset);
        var changed = false;
        foreach (var block in _graphs.Build(body).Blocks)
        {
            var operations = block.Instructions.Where(instruction => instruction.Operation != CilOperation.Nop).ToArray();
            for (var index = 1; index < operations.Length; index++)
            {
                if (operations[index] is { Operation: CilOperation.MaterializeType, Operand: CilOperand.None } materialize &&
                    operations[index - 1] is { Operation: CilOperation.LoadTypeToken } token)
                {
                    rewritten[materialize.Offset] = materialize with { Operand = token.Operand };
                    changed = true;
                }
            }
        }

        return changed ? body with { Instructions = [.. body.Instructions.Select(instruction => rewritten[instruction.Offset])] } : body;
    }
}
