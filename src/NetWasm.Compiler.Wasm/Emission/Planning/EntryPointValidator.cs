// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class EntryPointValidator : IEntryPointValidator
{
    public void Validate(MethodDefinitionModel entryPoint)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        if (entryPoint.Signature.ReturnSignatureType.StackKind == CliValueKind.ValueType)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                "a Wasm entry point cannot return a managed value type"));
        }
    }
}
