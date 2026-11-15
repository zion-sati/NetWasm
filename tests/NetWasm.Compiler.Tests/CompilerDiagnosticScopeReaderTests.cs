// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticScopeReaderTests
{
    [Fact]
    public void ReadReturnsTheCurrentScope()
    {
        var scope = new CompilerDiagnosticScope("compiler.jsonl");
        var state = new CompilerDiagnosticScopeState
        {
            Current =
            {
                Value = new CompilerDiagnosticScopeFrame(scope, Parent: null),
            },
        };
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(state));

        Assert.Same(scope, reader.Read());
    }

    [Fact]
    public void ReadReturnsNullWithoutAFrame()
    {
        var reader = Assert.IsAssignableFrom<ICompilerDiagnosticScopeReader>(
            new CompilerDiagnosticScopeReader(new CompilerDiagnosticScopeState()));

        Assert.Null(reader.Read());
    }
}
