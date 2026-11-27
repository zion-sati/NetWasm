// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ReducingReachabilityClosureBuilder(
    IReachabilityClosureBuilder inner,
    IReachableProgramReducer reducer,
    IAnalysisStepRecorder steps,
    ICompilerMemoryReleaser memory) : IReachabilityClosureBuilder
{
    private readonly IReachabilityClosureBuilder _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IReachableProgramReducer _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
    private readonly IAnalysisStepRecorder _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    private readonly ICompilerMemoryReleaser _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public ReachableProgram Build(MethodDefinitionModel? entryPoint, IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null, ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
    {
        ArgumentNullException.ThrowIfNull(requestedExports);
        var exports = requestedExports.ToArray();
        roots ??= ReachabilityRoots.Empty;
        var bodies = rewrittenBodies ?? ImmutableDictionary<string, CilMethodBody>.Empty;
        for (var round = 1; ; round++)
        {
            var program = _steps.Record($"closure {round}", () => _inner.Build(entryPoint, exports, roots, bodies));
            var reduced = _steps.Record($"reduction {round}", () => _reducer.Reduce(program, roots));
            if (reduced.IsEmpty) return program;
            bodies = bodies.SetItems(reduced);
            // The next round rebuilds the closure, so this one's is garbage: the collector's committed memory goes
            // back before it, rather than accumulating across rounds.
            program = null!;
            _memory.Release();
        }
    }
}
