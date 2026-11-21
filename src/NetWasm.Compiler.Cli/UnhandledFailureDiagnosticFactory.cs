// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal interface IUnhandledFailureDiagnosticFactory
{
    CompilerDiagnostic Create(Exception exception);
}

// The diagnostic for a failure no compiler stage reported. Reading inputs and writing outputs fail with I/O and access
// errors, the host's failures (NW1014); any other exception is a compiler defect (NW1011), so its type is kept and it
// is not mistaken for a missing input or an unwritable output.
internal sealed class UnhandledFailureDiagnosticFactory : IUnhandledFailureDiagnosticFactory
{
    public CompilerDiagnostic Create(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is IOException or UnauthorizedAccessException
            ? new CompilerDiagnostic(DiagnosticCode.HostFailure, exception.Message)
            : new CompilerDiagnostic(DiagnosticCode.CompilerInvariant,
                $"the compiler failed with {exception.GetType().FullName}: {exception.Message}");
    }
}
