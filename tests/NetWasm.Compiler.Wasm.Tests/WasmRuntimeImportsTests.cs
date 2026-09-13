using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmRuntimeImportsTests
{
    [Fact]
    public void CreatesEquivalentCatalogAndLegacyRuntimeImportViews()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();
        var imports = WasmRuntimeImports.Create();

        Assert.Equal(catalog.Imports.Select(import => import.Module),
            imports.Select(import => import.Module));
        Assert.Equal(catalog.Imports.Select(import => import.Name),
            imports.Select(import => import.Name));
        Assert.Equal(catalog.Imports.Select(import => import.Type.Result),
            imports.Select(import => import.Type.Result));
        Assert.Equal(catalog.Imports.Select(import => import.Type.Parameters.Length),
            imports.Select(import => import.Type.Parameters.Length));
        Assert.Equal(catalog.Imports.SelectMany(import => import.Type.Parameters),
            imports.SelectMany(import => import.Type.Parameters));
        Assert.All(catalog.Imports, import => Assert.True(
            import.Module is RuntimeAbi.RuntimeModule or RuntimeAbi.HostModule));
    }

    [Fact]
    public void CreatesNamedHostInteropImportFamiliesWithExpectedSignatures()
    {
        var strings = WasmRuntimeImports.CreateHostInteropStringResultImports();
        var handles = WasmRuntimeImports.CreateHostInteropHandleImports();
        var subscriptions = WasmRuntimeImports.CreateHostInteropSubscriptionImports();
        var bytes = WasmRuntimeImports.CreateHostInteropByteResultImports();

        Assert.Equal(
            [RuntimeAbi.HostInteropStringLength, RuntimeAbi.HostInteropCopyStringUtf16],
            strings.Select(import => import.Name));
        Assert.Equal([RuntimeAbi.HostInteropReleaseHandle],
            handles.Select(import => import.Name));
        Assert.Equal([RuntimeAbi.HostInteropReleaseSubscription],
            subscriptions.Select(import => import.Name));
        Assert.Equal([RuntimeAbi.HostInteropByteLength, RuntimeAbi.HostInteropCopyBytes],
            bytes.Select(import => import.Name));
        Assert.All(strings.Concat(handles).Concat(subscriptions).Concat(bytes), import =>
            Assert.Equal(RuntimeAbi.HostModule, import.Module));
        Assert.Equal(CliValueKind.I4, strings[0].Type.Result);
        Assert.Equal(CliValueKind.Void, handles[0].Type.Result);
    }
}
