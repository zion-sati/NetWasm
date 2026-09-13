using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Testing.CompilerHost;

internal sealed record CompilationRequest(
    string EntryAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    string EntryTypeName,
    string EntryMethodName,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target,
    string? DiagnosticTracePath,
    ImmutableArray<string> SourcePaths,
    ImmutableDictionary<string, string> ReferenceAssemblyAliases,
    string ModulePath,
    bool CaptureDiagnostic = false,
    string? WitPath = null,
    string? WitWorld = null,
    bool EmitStackTrace = false,
    string? StackTraceSymbolsPath = null);

internal sealed record CompilationResponse(
    ImmutableDictionary<int, string> TypeNames,
    string ModuleSha256,
    int StaticDataEnd);

internal sealed record CapturedDiagnosticResponse(
    int Code,
    string Message,
    string? Method,
    int? IlOffset);
