// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CompilerRejectionExpectation(CompilerDiagnostic Diagnostic, bool RequireFailureSnapshot = true);

internal interface ICompilerRejectionVerifier
{
    void Verify(CompilerRejectionExpectation expected, MalformedCompilationObservation observed);
}

internal sealed class CompilerRejectionVerifier : ICompilerRejectionVerifier
{
    public void Verify(CompilerRejectionExpectation expected, MalformedCompilationObservation observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(expected.Diagnostic);
        ArgumentNullException.ThrowIfNull(observed);
        if (observed.ResponseFailure is not null)
        {
            throw new InvalidOperationException("Compiler diagnostic response could not be read.", observed.ResponseFailure);
        }
        if (observed.Completion != QualifiedProcessCompletion.Exited || observed.ExitCode != 0)
        {
            throw new InvalidOperationException("Expected diagnostic capture did not complete successfully.");
        }
        if (observed.Diagnostic != expected.Diagnostic)
        {
            throw new InvalidOperationException("Compiler rejection did not match the exact expected diagnostic.");
        }
        if (observed.ModuleExists)
        {
            throw new InvalidOperationException("Compiler rejection left an output module.");
        }
        if (expected.RequireFailureSnapshot && !observed.FailureSnapshotExists)
        {
            throw new InvalidOperationException("Compiler rejection did not retain its required failure snapshot.");
        }
    }
}
