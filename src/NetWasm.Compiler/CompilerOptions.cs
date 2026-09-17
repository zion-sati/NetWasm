using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler;

public enum CompilerEntryPointKind
{
    RawFunction,
    ManagedExecutable,
}

public sealed record RequestedExport(string Name, string TypeName, string MethodName);

public sealed record CompilerOptions(
    string EntryAssemblyPath,
    ImmutableArray<string> ReferencePaths,
    string EntryTypeName,
    string EntryMethodName,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target = WasmTarget.Wasm32,
    string? DiagnosticTracePath = null,
    string? DiagnosticLogPath = null,
    string? WitPath = null,
    string? WitWorld = null,
    ImmutableArray<string> SourcePaths = default,
    ImmutableDictionary<string, string>? ReferenceAssemblyAliases = null,
    bool EmitExceptionTypeMap = true,
    bool EmitStackTrace = false,
    int? EntryMethodToken = null,
    CompilerEntryPointKind EntryPointKind = CompilerEntryPointKind.RawFunction,
    ICompilerMetricsObserver? MetricsObserver = null,
    bool EnableFrontendCache = false,
    string? IntermediateOutputPath = null);

public sealed partial record CompilationResult(
    byte[] ApplicationModule,
    ReachableProgram Program,
    ManagedLayoutSnapshot Layouts,
    HostInteropManifest InteropManifest,
    int StaticDataEnd)
{
    public ImmutableArray<WasmFunctionImport> FunctionImports { get; init; } = [];
    public ImmutableArray<string> RuntimeFeatures { get; init; } = [];
}
