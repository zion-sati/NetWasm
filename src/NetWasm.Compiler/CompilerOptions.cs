using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler;

public enum CompilerEntryPointKind
{
    RawFunction,
    ManagedExecutable,
    Library,
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
    bool EmitTypeNames = false,
    bool EmitStackTrace = false,
    int? EntryMethodToken = null,
    CompilerEntryPointKind EntryPointKind = CompilerEntryPointKind.RawFunction,
    ICompilerMetricsObserver? MetricsObserver = null,
    string? IntermediateOutputPath = null,
    bool UseJavaScriptExportBoundary = false,
    string? ProjectDirectory = null,
    string? PathMap = null,
    bool StructuredDiagnostics = false,
    FinalWasmOptimization Optimization = FinalWasmOptimization.Oz,
    WasmOptimizationOverrides? OptimizationOverrides = null,
    ImmutableArray<string> MetadataReferencePaths = default)
{
    /// <summary>Original declaration images used for contract validation, never executable references.</summary>
    public ImmutableArray<string> OriginalContractReferencePaths { get; init; } = [];

    public ImmutableArray<CompilationReferenceOrigin> ReferenceOrigins { get; init; } = [];
    // Keep and reuse compiled units under IntermediateOutputPath. nwc and the SDK turn them
    // on by default at O0; at an optimizing level they are reused only when asked for.
    public bool CompiledUnits { get; init; }

    // Emit every method a unit restores as well, and fail the compile when the two bodies
    // differ: proves units sound on a workload (tests, diagnosis). Costs a full emission.
    public bool VerifyCompiledUnits { get; init; }

    // Modules the project's native archives import from; the host supplies
    // them beside the modules [WasmImportLinkage] imports name.
    public ImmutableArray<string> ImportModules { get; init; } = [];

    // The native libraries the program links, by NetWasmLibraryName. When given, a P/Invoke into any other library
    // compiles to a body that throws DllNotFoundException (NativeLibraryBinding); when null, every P/Invoke binds.
    public ImmutableArray<string>? LinkedNativeLibraries { get; init; }

    // The project's feature switches the compiler folds: Trim="true" RuntimeHostConfigurationOption items with a
    // Boolean value. A call to a [FeatureSwitchDefinition] getter of one becomes its value.
    public ImmutableDictionary<string, bool> FeatureSwitches { get; init; } = ImmutableDictionary<string, bool>.Empty;

    // The project's RuntimeHostConfigurationOption items, in name order: AppContext reads them as .NET reads
    // runtimeconfig.json's properties.
    public ImmutableArray<(string Name, string Value)> RuntimeProperties { get; init; } = [];

    // A plain core module (a Library build of a WIT world): it imports only the world's
    // functions, with no WASI and no Component Model event loop, for engines without the
    // Component Model. Its platform services are the program's bindings or throw.
    public bool PlainCoreModule { get; init; }

    // The program's deployed file name, <assembly>.wasm as the SDK names it, which its sidecars' names
    // extend (App.wasm.type-symbols) and under which the program opens them.
    public string ProgramFileName =>
        System.IO.Path.GetFileNameWithoutExtension(EntryAssemblyPath) + ".wasm";
}

public sealed partial record CompilationResult(
    byte[] ApplicationModule,
    ReachableProgram Program,
    ManagedLayoutSnapshot Layouts,
    HostInteropManifest InteropManifest,
    int StaticDataEnd)
{
    public ImmutableArray<WasmFunctionImport> FunctionImports { get; init; } = [];
    public ImmutableArray<WasmNativeImport> NativeImports { get; init; } = [];
    public ImmutableArray<string> RuntimeFeatures { get; init; } = [];
    public WasmNativeCallbackSupportArtifact? NativeCallbackSupport { get; init; }
}
