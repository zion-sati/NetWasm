using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed record ManagedModuleCompileRequest(
    string InputAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    ImmutableArray<string> SourcePaths,
    string Target,
    string? DiagnosticTracePath,
    string? DiagnosticLogPath,
    bool EmitStackTrace,
    string Optimization,
    string? IntermediateOutputPath,
    string? WitPath = null,
    string? WitWorld = null,
    bool CompileAsLibrary = false,
    bool UseJavaScriptExportBoundary = false,
    string? ProjectDirectory = null,
    string? PathMap = null,
    bool StructuredDiagnostics = false,
    WasmOptimizationOverrides? OptimizationOverrides = null)
{
    public ImmutableArray<string> ImportModules { get; init; } = [];
}
