using NetWasm.Compiler.Tasks.Compilation;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed record CompilerArtifactWriteRequest(
    ManagedModuleCompilation Compilation,
    string CoreModulePath,
    string RuntimeLayoutPath,
    string InteropManifestPath,
    string CompilerMetadataPath,
    string NativeCallbackObjectPath,
    string? StackTraceSymbolsPath)
{
    public string? ExceptionTypeMapPath { get; init; }
}
