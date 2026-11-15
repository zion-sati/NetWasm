// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticScopeReader(CompilerDiagnosticScopeState state)
    : ICompilerDiagnosticScopeReader
{
    public CompilerDiagnosticScope? Read() => state.Current.Value?.Scope;
}
