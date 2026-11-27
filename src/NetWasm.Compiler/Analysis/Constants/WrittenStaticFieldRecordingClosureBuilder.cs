// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// Records, on the settled program, the static fields its reachable code writes: the set the unwritten-field fold
// folded every other field's loads by, since the reductions stop at the round that changes nothing.
internal sealed class WrittenStaticFieldRecordingClosureBuilder(
    IReachabilityClosureBuilder inner,
    IStaticFieldWriteCollector writes) : IReachabilityClosureBuilder
{
    private readonly IReachabilityClosureBuilder _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IStaticFieldWriteCollector _writes = writes ?? throw new ArgumentNullException(nameof(writes));

    public ReachableProgram Build(MethodDefinitionModel? entryPoint, IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null, ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
    {
        roots ??= ReachabilityRoots.Empty;
        var program = _inner.Build(entryPoint, requestedExports, roots, rewrittenBodies);
        return program with { WrittenStaticFields = _writes.Collect(program, roots) };
    }
}
