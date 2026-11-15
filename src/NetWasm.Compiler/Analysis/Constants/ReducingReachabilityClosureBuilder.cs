// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ReducingReachabilityClosureBuilder(
    IReachabilityClosureBuilder inner,
    IReachableProgramReducer reducer) : IReachabilityClosureBuilder
{
    private readonly IReachabilityClosureBuilder _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IReachableProgramReducer _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));

    public ReachableProgram Build(MethodDefinitionModel? entryPoint, IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null, ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
    {
        ArgumentNullException.ThrowIfNull(requestedExports);
        var exports = requestedExports.ToArray();
        roots ??= ReachabilityRoots.Empty;
        var bodies = rewrittenBodies ?? ImmutableDictionary<string, CilMethodBody>.Empty;
        while (true)
        {
            var program = _inner.Build(entryPoint, exports, roots, bodies);
            var reduced = _reducer.Reduce(program, roots);
            if (reduced.IsEmpty) return program;
            bodies = bodies.SetItems(reduced);
        }
    }
}
