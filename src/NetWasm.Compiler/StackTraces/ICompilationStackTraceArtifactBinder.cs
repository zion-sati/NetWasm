using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

internal interface ICompilationStackTraceArtifactBinder
{
    CompilationResult Bind(
        CompilationResult result,
        ImmutableArray<WasmStackTraceSymbol> symbols,
        CompilerOptions options);
}
