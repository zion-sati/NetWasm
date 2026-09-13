using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

internal sealed class CompilationStackTraceArtifactBinder(
    IStackTraceSymbolWriter symbols) : ICompilationStackTraceArtifactBinder
{
    public CompilationResult Bind(
        CompilationResult result,
        ImmutableArray<WasmStackTraceSymbol> methodSymbols,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        return result with
        {
            StackTraceSymbols = options.EmitStackTrace
                ? symbols.Write(methodSymbols)
                : null,
        };
    }
}
