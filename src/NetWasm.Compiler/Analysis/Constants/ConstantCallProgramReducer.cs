// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// Folds constant calls in every reachable body, including the calls that
// inlining exposed after the per-method fold ran.
internal sealed class ConstantCallProgramReducer(IMethodRewriteRule folding) : IReachableProgramReducer
{
    private readonly IMethodRewriteRule _folding = folding ?? throw new ArgumentNullException(nameof(folding));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in program.Methods.Values.Concat(program.ConstructedMethods.Values).DistinctBy(body => body.Method.CanonicalName))
        {
            var folded = _folding.Rewrite(method.Body);
            if (!ReferenceEquals(folded, method.Body)) result.Add(method.Method.CanonicalName, folded);
        }
        return result.ToImmutable();
    }
}
