using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Symbols;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Cli;

internal sealed record ManagedModuleCompilation(
    byte[] CoreModule,
    int StaticDataEnd,
    HostInteropManifest InteropManifest,
    string Target,
    ManagedExecutableEntryPointAbi? EntryPoint = null,
    ImmutableArray<string> RuntimeFeatures = default,
    ImmutableArray<WasmFunctionImport> FunctionImports = default)
{
    public ImmutableArray<WasmNativeImport> NativeImports { get; init; } = [];
    public WasmNativeCallbackSupportArtifact? NativeCallbackSupport { get; init; }
    public SymbolSidecarArtifact? TypeSymbols { get; init; }
    public SymbolSidecarArtifact? MethodSymbols { get; init; }
    // Whether the compiled entry export completes before it returns. A world without the
    // Component Model's event loop (a plain core module) runs an asynchronous Main this way.
    public bool SynchronousEntry { get; init; } = true;
}
