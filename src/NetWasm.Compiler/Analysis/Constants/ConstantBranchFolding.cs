using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Constant branches fold, the code they cut off goes and jumps across it fall
// through, round after round, so a constant pushed in one block reaches the
// comparison a folded check had put in a later one.
internal sealed class ConstantBranchFolding(IControlFlowGraphBuilder graphs) : IMethodRewriteRule
{
    private const int MaximumRounds = 8;

    private readonly IMethodRewriteRule[] _rules =
    [
        new ConstantBranchRewriter(graphs),
        new UnreachableInstructionRewriter(graphs),
        new DeadJumpRewriter(graphs),
    ];

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        for (var round = 0; round < MaximumRounds; round++)
        {
            var next = body;
            foreach (var rule in _rules) next = rule.Rewrite(next);
            if (ReferenceEquals(next, body)) break;
            body = next;
        }
        return body;
    }
}
