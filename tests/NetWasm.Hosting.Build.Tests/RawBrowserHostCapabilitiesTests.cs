using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

// Issue #121: a raw page or worker host bundles only the platform providers its
// application links, and the filesystem scope only when it links a filesystem.
public sealed class RawBrowserHostCapabilitiesTests
{
    private static readonly string[] MinimalPageModules =
    [
        "wasi:cli/environment@0.3.2",
        "wasi:cli/stderr@0.3.2",
        "wasi:cli/stdout@0.3.2",
        "wasi:random/random@0.3.2",
    ];

    [Fact]
    public void SelectsTheShippedCatalogsProvidersForAMinimalPage()
    {
        var capabilities = RawBrowserHostCapabilities.Select(
            HostingCatalogFixture.ShippedHostingRoot(), [.. MinimalPageModules, "netwasm.host.v1"]);

        Assert.False(capabilities.LinksFilesystem);
        using var catalog = JsonDocument.Parse(capabilities.ProviderCatalogJson);
        Assert.Equal(2, catalog.RootElement.GetProperty("schema").GetInt32());
        Assert.False(catalog.RootElement.TryGetProperty("shim", out _));
        Assert.Equal(MinimalPageModules,
            catalog.RootElement.GetProperty("providers").EnumerateArray()
                .Select(provider => provider.GetProperty("module").GetString()));
        Assert.All(catalog.RootElement.GetProperty("providers").EnumerateArray(),
            provider => Assert.NotEqual(0, provider.GetProperty("functions").GetArrayLength()));
    }

    [Fact]
    public void AFilesystemInterfaceBringsTheFilesystemScope()
    {
        using var directory = TemporaryDirectory.Create();
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting);

        var capabilities = RawBrowserHostCapabilities.Select(hosting, ["wasi:filesystem/types@0.3.2"]);
        var source = new StringBuilder();
        BrowserHostSource.AppendRawImports(source, JavaScriptRuntime.Page, capabilities);
        BrowserHostSource.AppendRawPlatformMembers(source, "", capabilities);

        Assert.True(capabilities.LinksFilesystem);
        var text = source.ToString();
        Assert.Contains("import { createWasiPlatform, webPlatform } from \"netwasm:runtime/page\";", text,
            StringComparison.Ordinal);
        Assert.Contains("import { createBrowserFileMounts, createWasiFilesystem } from \"netwasm:runtime/filesystem\";", text,
            StringComparison.Ordinal);
        // The in-memory tree (#142): a read-write root holding /tmp and what the page mounts.
        Assert.Contains("createFilesystemScope: preopens => createWasiFilesystem(Object.freeze({ hostFiles: fileMounts.hostFiles, " +
            "preopens: Object.freeze([...preopens, Object.freeze({ access: \"readWrite\", guestPath: \"/\", hostPath: \"/\" })]) })),",
            text, StringComparison.Ordinal);

        var page = new StringBuilder();
        BrowserHostSource.AppendFileMounts(page, worker: false);
        Assert.Equal(
            "const fileMounts = createBrowserFileMounts(Object.freeze({ readBlobSync: null }));\n" +
            "export const { mount, unmount, sync } = fileMounts;\n",
            page.ToString().ReplaceLineEndings("\n"));
        var worker = new StringBuilder();
        BrowserHostSource.AppendFileMounts(worker, worker: true);
        Assert.Contains("new FileReaderSync().readAsArrayBuffer(blob.slice(start, end))", worker.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("export", worker.ToString(), StringComparison.Ordinal);
        Assert.Contains("createImports: createWasiPlatform(Object.freeze({ initialDirectory: null, network: null })),", text, StringComparison.Ordinal);

        var minimal = new StringBuilder();
        var withoutFilesystem = RawBrowserHostCapabilities.Select(hosting, ["wasi:cli/stdout@0.3.2"]);
        BrowserHostSource.AppendRawImports(minimal, JavaScriptRuntime.Page, withoutFilesystem);
        BrowserHostSource.AppendRawPlatformMembers(minimal, "", withoutFilesystem);
        Assert.DoesNotContain("netwasm:runtime/filesystem", minimal.ToString(), StringComparison.Ordinal);
        Assert.Contains("createFilesystemScope: null,", minimal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnHttpInterfaceBringsHttpOverFetch()
    {
        var capabilities = RawBrowserHostCapabilities.Select(
            HostingCatalogFixture.ShippedHostingRoot(), ["wasi:http/client@0.3.2", "wasi:http/types@0.3.2"]);
        var source = new StringBuilder();
        BrowserHostSource.AppendRawImports(source, JavaScriptRuntime.Worker, capabilities);
        BrowserHostSource.AppendRawPlatformMembers(source, "", capabilities);

        Assert.True(capabilities.LinksHttp);
        Assert.False(capabilities.LinksFilesystem);
        var text = source.ToString();
        Assert.Contains("import { createWasiHttpFetch } from \"netwasm:runtime/http\";", text, StringComparison.Ordinal);
        Assert.Contains(
            "createImports: createWasiPlatform(Object.freeze({ initialDirectory: null, network: Object.freeze({ modules: createWasiHttpFetch(Object.freeze({ fetch: webPlatform.fetch })) }) })),",
            text, StringComparison.Ordinal);
        BrowserApiBindings.AssertNoneAtImport(text);
        Assert.False(RawBrowserHostCapabilities.Select(
            HostingCatalogFixture.ShippedHostingRoot(), MinimalPageModules).LinksHttp);
    }

    [Fact]
    public void ARequiredInterfaceWithoutAProviderFailsTheBuild()
    {
        using var directory = TemporaryDirectory.Create();
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting, "wasi:cli/stdout@0.3.2");

        var missing = Assert.Throws<ArgumentException>(() =>
            RawBrowserHostCapabilities.Select(hosting, ["wasi:random/random@0.3.2"]));
        Assert.Contains("'wasi:random/random@0.3.2' has no hosting platform provider", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsACatalogWithWindowsLineEndingsAndRejectsAnUnexpectedFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting);
        var catalogPath = Path.Combine(hosting, "platform-provider-catalog.generated.mjs");
        File.WriteAllText(catalogPath, File.ReadAllText(catalogPath).ReplaceLineEndings("\r\n"));

        Assert.False(RawBrowserHostCapabilities.Select(hosting, ["wasi:cli/stdout@0.3.2"]).LinksFilesystem);

        File.WriteAllText(catalogPath, "export const platformProviderCatalog = {};\n");
        Assert.Throws<InvalidOperationException>(() => RawBrowserHostCapabilities.Select(hosting, []));
    }
}
