using NetWasm.Compiler.Symbols;

namespace NetWasm.Compiler;

public sealed partial record CompilationResult
{
    // <program>.wasm.type-symbols, which every compile writes (issue 225).
    public SymbolSidecarArtifact? TypeSymbols { get; init; }

    // <program>.wasm.method-symbols, with NetWasmManagedStackTrace.
    public SymbolSidecarArtifact? MethodSymbols { get; init; }
}
