using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class MethodSpecializer(
    IEnumerable<IMethodRewriteRule> rules) : IMethodSpecializer
{
    private readonly IMethodRewriteRule[] _rules =
        rules is null
            ? throw new ArgumentNullException(nameof(rules))
            : [.. rules];

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var rule in _rules)
        {
            body = rule.Rewrite(body);
        }
        return body;
    }
}
