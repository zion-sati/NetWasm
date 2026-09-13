using NetWasm.Compiler.StackTraces;

namespace NetWasm.Compiler;

public sealed partial record CompilationResult
{
    public StackTraceSymbolArtifact? StackTraceSymbols { get; init; }
}
