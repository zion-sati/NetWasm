using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Tests.TestSupport;

internal static class CompilerTaskTestData
{
    public static ManagedModuleCompilation CreateCompilation(
        string target = "wasm32",
        StackTraceSymbolArtifact? symbols = null,
        ManagedExecutableEntryPointAbi? entryPoint = null,
        ImmutableArray<string> runtimeFeatures = default,
        ImmutableArray<WasmFunctionImport> functionImports = default) => new(
        [0, 97, 115, 109],
        65_537,
        new HostInteropManifest(
            1,
            target,
            new HostInteropStatusAbi(0, 1, 8),
            new HostInteropTargetLayout(4, 4, 8, 4, 8),
            [],
            []),
        symbols,
        target,
        entryPoint,
        runtimeFeatures,
        functionImports);
}
