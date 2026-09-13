using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

public interface IStackTraceSymbolWriter
{
    StackTraceSymbolArtifact Write(
        ImmutableArray<WasmStackTraceSymbol> symbols);
}
