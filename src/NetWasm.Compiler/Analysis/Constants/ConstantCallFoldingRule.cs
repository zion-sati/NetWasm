// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Replaces calls whose constant arguments determine their result, then
// removes the branches and code those results decide.
internal sealed class ConstantCallFoldingRule(
    IMethodRewriteRule calls,
    IMethodSpecializer cleanup) : IMethodRewriteRule
{
    private readonly IMethodRewriteRule _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly IMethodSpecializer _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var folded = _calls.Rewrite(body);
        return ReferenceEquals(folded, body) ? body : _cleanup.Rewrite(folded);
    }
}
