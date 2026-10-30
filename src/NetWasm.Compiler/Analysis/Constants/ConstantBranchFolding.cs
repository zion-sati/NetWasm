using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Constant branches fold, the code they cut off goes and jumps across it fall
// through, round after round, so a constant pushed in one block reaches the
// comparison a folded check had put in a later one.
internal sealed class ConstantBranchFolding(IReadOnlyList<IMethodRewriteRule> rules) : IMethodRewriteRule
{
    private const int MaximumRounds = 8;

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        for (var round = 0; round < MaximumRounds; round++)
        {
            var next = body;
            foreach (var rule in rules) next = rule.Rewrite(next);
            if (ReferenceEquals(next, body)) break;
            body = next;
        }
        return body;
    }
}
