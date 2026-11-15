// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerComplexityMetricsCollector
{
    CompilerComplexityReport Collect(
        ISymbolFormatter symbols,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        ImmutableArray<WasmManagedMethodEmissionMetric> emissions);
}
