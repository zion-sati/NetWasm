// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticMethodTraceFormatter
{
    string FormatMethods(
        ISymbolFormatter symbols,
        ReachableProgram program,
        WasmMethodLoweringResult lowering);
}
