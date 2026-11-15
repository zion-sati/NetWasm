// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Threading;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticScopeState
{
    public AsyncLocal<CompilerDiagnosticScopeFrame?> Current { get; } = new();
}

internal sealed record CompilerDiagnosticScopeFrame(
    CompilerDiagnosticScope Scope,
    CompilerDiagnosticScopeFrame? Parent);
