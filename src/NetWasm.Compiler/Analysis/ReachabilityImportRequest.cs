// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis;

internal sealed record ReachabilityImportRequest(
    MethodInstanceModel Method,
    bool IsOutwardBoundary = false,
    bool IsProcessEntryPoint = false)
{
    public ImmutableStack<CompilerDependencyStep> DependencyPath { get; init; } = ImmutableStack<CompilerDependencyStep>.Empty;
}
