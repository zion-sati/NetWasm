// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

// A failure no compiler stage reported: I/O and access errors are the host's (NW1014); anything else is a compiler
// defect (NW1011), not a missing input or an unwritable output (issue #259 read as a host failure).
public sealed class UnhandledFailureDiagnosticFactoryTests
{
    [Theory]
    [MemberData(nameof(HostFailures))]
    public void InputAndOutputFailuresAreTheHosts(Exception failure)
    {
        var diagnostic = Assert.IsAssignableFrom<IUnhandledFailureDiagnosticFactory>(
            new UnhandledFailureDiagnosticFactory()).Create(failure);

        Assert.Equal(DiagnosticCode.HostFailure, diagnostic.Code);
        Assert.Equal(failure.Message, diagnostic.Message);
    }

    [Fact]
    public void AnyOtherFailureIsACompilerDefectThatKeepsItsType()
    {
        var diagnostic = new UnhandledFailureDiagnosticFactory().Create(
            new InvalidOperationException("Sequence contains more than one element"));

        Assert.Equal(DiagnosticCode.CompilerInvariant, diagnostic.Code);
        Assert.Equal(
            "NW1011: the compiler failed with System.InvalidOperationException: Sequence contains more than one element",
            diagnostic.ToString());
    }

    [Fact]
    public void CreateRejectsAMissingFailure()
    {
        Assert.Throws<ArgumentNullException>(() => new UnhandledFailureDiagnosticFactory().Create(null!));
    }

    public static TheoryData<Exception> HostFailures() => new()
    {
        new FileNotFoundException("Could not find file 'App.dll'."),
        new DirectoryNotFoundException("Could not find a part of the path 'out/'."),
        new UnauthorizedAccessException("Access to the path 'app.wasm' is denied."),
    };
}
