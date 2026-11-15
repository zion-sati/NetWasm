// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticLogPathResolverTests
{
    [Fact]
    public void ResolveUsesExplicitLogPathBeforeTraceBundle()
    {
        var resolver = Assert.IsAssignableFrom<ICompilerDiagnosticLogPathResolver>(
            new CompilerDiagnosticLogPathResolver());

        var actual = resolver.Resolve(Options(
            diagnosticLogPath: "explicit.jsonl",
            diagnosticTracePath: "trace.json"));

        Assert.Equal("explicit.jsonl", actual);
    }

    [Fact]
    public void ResolvePlacesDefaultLogInsideTraceBundle()
    {
        var resolver = Assert.IsAssignableFrom<ICompilerDiagnosticLogPathResolver>(
            new CompilerDiagnosticLogPathResolver());

        var actual = resolver.Resolve(Options(
            diagnosticLogPath: null,
            diagnosticTracePath: "trace.json"));

        Assert.Equal(
            Path.Combine("trace.json.passes", "compiler.log.jsonl"),
            actual);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void ResolveDisablesLoggingWithoutAConfiguredPath(
        string? diagnosticLogPath,
        string? diagnosticTracePath)
    {
        var resolver = Assert.IsAssignableFrom<ICompilerDiagnosticLogPathResolver>(
            new CompilerDiagnosticLogPathResolver());

        var actual = resolver.Resolve(Options(
            diagnosticLogPath,
            diagnosticTracePath));

        Assert.Null(actual);
    }

    [Fact]
    public void ResolveRejectsNullOptions()
    {
        var resolver = Assert.IsAssignableFrom<ICompilerDiagnosticLogPathResolver>(
            new CompilerDiagnosticLogPathResolver());

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
    }

    private static CompilerOptions Options(
        string? diagnosticLogPath,
        string? diagnosticTracePath) => new(
            "application.dll",
            [],
            "Program",
            "Main",
            [],
            DiagnosticTracePath: diagnosticTracePath,
            DiagnosticLogPath: diagnosticLogPath);
}
