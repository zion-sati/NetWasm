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
