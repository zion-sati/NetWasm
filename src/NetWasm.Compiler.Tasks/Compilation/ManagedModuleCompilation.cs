using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed record ManagedModuleCompilation(
    byte[] CoreModule,
    int StaticDataEnd,
    HostInteropManifest InteropManifest,
    StackTraceSymbolArtifact? StackTraceSymbols,
    string Target,
    ManagedExecutableEntryPointAbi? EntryPoint = null,
    ImmutableArray<string> RuntimeFeatures = default,
    ImmutableArray<WasmFunctionImport> FunctionImports = default);
