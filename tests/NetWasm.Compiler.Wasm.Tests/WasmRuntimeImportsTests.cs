// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmRuntimeImportsTests
{
    [Fact]
    public void CatalogImportsComeFromTheRuntimeOrTheHost()
    {
        var catalog = WasmRuntimeImports.CreateCatalog();

        Assert.NotEmpty(catalog.Imports);
        Assert.All(catalog.Imports, import => Assert.True(
            import.Module is RuntimeAbi.RuntimeModule or RuntimeAbi.HostModule));
    }

    [Fact]
    public void CreatesNamedHostInteropImportFamiliesWithExpectedSignatures()
    {
        var strings = WasmRuntimeImports.CreateHostInteropStringResultImports();
        var handles = WasmRuntimeImports.CreateHostInteropHandleImports();
        var subscriptions = WasmRuntimeImports.CreateHostInteropSubscriptionImports();

        Assert.Equal(
            [RuntimeAbi.HostInteropStringLength, RuntimeAbi.HostInteropCopyStringUtf16],
            strings.Select(import => import.Name));
        Assert.Equal([RuntimeAbi.HostInteropReleaseHandle],
            handles.Select(import => import.Name));
        Assert.Equal([RuntimeAbi.HostInteropReleaseSubscription],
            subscriptions.Select(import => import.Name));
        Assert.All(strings.Concat(handles).Concat(subscriptions), import =>
            Assert.Equal(RuntimeAbi.HostModule, import.Module));
        Assert.Equal(CliValueKind.I4, strings[0].Type.Result);
        Assert.Equal(CliValueKind.Void, handles[0].Type.Result);
    }
}
