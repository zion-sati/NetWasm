using System.Collections.Immutable;
using System.Text.Json.Serialization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Cli;

// The runtime layout sidecar: schema 3, or schema 4 with native callback support.
internal sealed record CompilerRuntimeLayout(
    int SchemaVersion,
    string Target,
    int ApplicationStaticDataEnd,
    ManagedExecutableEntryPointAbi? ManagedExecutableEntryPoint,
    string[] RuntimeFeatures,
    ImmutableArray<WasmNativeImport> NativeImports,
    ImmutableArray<string> ImportModules)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CompilerNativeCallbackSupport? NativeCallbackSupport { get; init; }
}

internal sealed record CompilerNativeCallbackSupport(
    string FileName,
    string Sha256,
    ImmutableArray<WasmNativeCallbackDescriptor> Callbacks,
    ImmutableArray<string> TemporaryApplicationExports,
    ImmutableArray<string> TemporaryRuntimeExports);

internal sealed record CompilerMetadataSidecar(
    int SchemaVersion,
    string Target,
    string[] RuntimeFeatures,
    CompilerFunctionImportSidecar[] FunctionImports);

internal sealed record CompilerFunctionImportSidecar(
    string Module,
    string Name,
    ImmutableArray<CliValueKind> Parameters,
    CliValueKind Result);
