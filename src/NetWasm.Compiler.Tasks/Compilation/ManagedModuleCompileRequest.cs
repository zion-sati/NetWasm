using System.Collections.Immutable;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed record ManagedModuleCompileRequest(
    string InputAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    ImmutableArray<string> SourcePaths,
    string Target,
    string? DiagnosticTracePath,
    string? DiagnosticLogPath,
    bool EmitStackTrace,
    string? WitPath = null,
    string? WitWorld = null);
