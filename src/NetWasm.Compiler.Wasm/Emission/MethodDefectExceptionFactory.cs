// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

// A compiler defect met while compiling one method (NW1011): the diagnostic names the method, its context is the
// method's key so the compilation adds the package it came from, and the defect is the cause.
internal sealed class MethodDefectExceptionFactory : IMethodDefectExceptionFactory
{
    public CompilerException Create(MethodDefinitionModel method, CliTypeIdentity? declaringType, string work,
        Exception cause)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrEmpty(work);
        ArgumentNullException.ThrowIfNull(cause);
        var type = declaringType?.FullName ?? method.DeclaringType.ToString();
        return new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.CompilerInvariant,
            $"{work} the method failed with {cause.GetType().FullName}: {cause.Message}",
            $"{type}.{method.Name}")
        {
            Context = new CompilerDiagnosticContext(method.Key),
        }, cause);
    }
}
