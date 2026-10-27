using System.Collections.Immutable;
using System.Security.Cryptography;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Cli.Tests.TestSupport;

internal static class CliTestData
{
    public static ManagedModuleCompilation Compilation(
        string target = "wasm32",
        StackTraceSymbolArtifact? symbols = null,
        ManagedExecutableEntryPointAbi? entryPoint = null,
        ImmutableArray<string> runtimeFeatures = default,
        ImmutableArray<WasmFunctionImport> functionImports = default,
        ImmutableArray<WasmNativeImport> nativeImports = default,
        WasmNativeCallbackSupportArtifact? nativeCallbackSupport = null) => new(
        [0, 97, 115, 109],
        65_537,
        InteropManifest(target),
        symbols,
        target,
        entryPoint,
        runtimeFeatures,
        functionImports)
        {
            NativeImports = nativeImports.IsDefault ? [] : nativeImports,
            NativeCallbackSupport = nativeCallbackSupport,
        };

    public static HostInteropManifest InteropManifest(string target) => new(
        1,
        target,
        new HostInteropStatusAbi(0, 1, 8),
        new HostInteropTargetLayout(4, 4, 8, 4, 8, 0, 4),
        [],
        []);

    public static WasmNativeCallbackSupportArtifact CallbackSupport(
        WasmValueType addressType,
        WasmValueType? returnType = WasmValueType.I32)
    {
        var bytes = new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 };
        return new(
            bytes,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            [new("native_callback", "runtime_callback", "application_callback", "callback_address",
                [WasmValueType.I32, addressType], returnType)],
            ["application_callback"],
            ["callback_address"]);
    }
}
