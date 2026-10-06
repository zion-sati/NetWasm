using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// Application output and its host contract, without compiler reachability or layout state.
/// The returned application byte array is owned by the caller.
/// </summary>
public sealed record BrowserCompilationResult(
    byte[] ApplicationModule,
    int StaticDataEnd,
    ImmutableArray<string> RuntimeFeatures,
    ImmutableArray<WasmFunctionImport> FunctionImports,
    HostInteropManifest InteropManifest,
    BrowserCompilationEntryPoint EntryPoint)
{
    public CompilerMetricsReport? CompilerMetrics { get; init; }

    public CompilerAdapterTiming? CompilerTiming { get; init; }

    /// <summary>Reached static native calls that require caller-supplied archive providers.</summary>
    public ImmutableArray<WasmNativeImport> NativeImports { get; init; } = [];

    /// <summary>Optional native callback object owned by the caller after compilation.</summary>
    public WasmNativeCallbackSupportArtifact? NativeCallbackSupport { get; init; }
}
