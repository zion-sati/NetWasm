// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Decorator: once the reductions settle the program, it carries the attribute queries its code leaves to their run-time
// paths, which the build reports (NW1019).
internal sealed class UnplannedAttributeQueryClosureBuilder(
    IReachabilityClosureBuilder inner,
    IUnplannedAttributeQueryFinder finder) : IReachabilityClosureBuilder
{
    private readonly IReachabilityClosureBuilder _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnplannedAttributeQueryFinder _finder = finder ?? throw new ArgumentNullException(nameof(finder));

    public ReachableProgram Build(MethodDefinitionModel? entryPoint, IEnumerable<ProgramExport> requestedExports,
        ReachabilityRoots? roots = null, ImmutableDictionary<string, CilMethodBody>? rewrittenBodies = null)
    {
        var program = _inner.Build(entryPoint, requestedExports, roots, rewrittenBodies);
        return program with { UnplannedAttributeQueries = _finder.Find(program) };
    }
}
