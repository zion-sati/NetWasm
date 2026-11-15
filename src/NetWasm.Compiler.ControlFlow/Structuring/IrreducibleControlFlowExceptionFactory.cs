// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class IrreducibleControlFlowExceptionFactory : IIrreducibleControlFlowExceptionFactory
{
    public CompilerException Create(ControlFlowStructuringState state, string message) => new(
        new CompilerDiagnostic(
            DiagnosticCode.IrreducibleControlFlow,
            message,
            state.Validated!.Graph.MethodBody.Method.Name));
}
