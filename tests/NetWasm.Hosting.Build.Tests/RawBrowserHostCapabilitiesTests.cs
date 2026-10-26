using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

// Issue #121: a raw page or worker host bundles only the Preview 2 shim
// families and platform providers its application links.
public sealed class RawBrowserHostCapabilitiesTests
{
    private static readonly string[] MinimalPageModules =
    [
        "wasi:cli/environment@0.2.11",
        "wasi:cli/stderr@0.2.11",
        "wasi:io/streams@0.2.11",
        "wasi:random/random@0.2.11",
    ];

    [Fact]
    public void SelectsTheShippedCatalogsProvidersForAMinimalPage()
    {
        var capabilities = RawBrowserHostCapabilities.Select(
            HostingCatalogFixture.ShippedHostingRoot(), [.. MinimalPageModules, "netwasm.host.v1"]);

        Assert.Equal(["cli", "io", "random"], capabilities.ShimFamilies.ToArray());
        Assert.False(capabilities.LinksFilesystem);
        using var catalog = JsonDocument.Parse(capabilities.ProviderCatalogJson);
        Assert.Equal(1, catalog.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal("@bytecodealliance/preview2-shim", catalog.RootElement.GetProperty("shim").GetProperty("package").GetString());
        Assert.Equal(MinimalPageModules,
            catalog.RootElement.GetProperty("providers").EnumerateArray()
                .Select(provider => provider.GetProperty("module").GetString()));
        Assert.All(catalog.RootElement.GetProperty("providers").EnumerateArray(),
            provider => Assert.NotEqual(0, provider.GetProperty("functions").GetArrayLength()));
    }

    [Fact]
    public void AFilesystemInterfaceBringsTheFilesystemFamilyAndScope()
    {
        using var directory = TemporaryDirectory.Create();
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting);

        var capabilities = RawBrowserHostCapabilities.Select(hosting, ["wasi:filesystem/types@0.2.11"]);
        var source = new StringBuilder();
        BrowserHostSource.AppendRawImports(source, hosting, Path.Combine(directory.Path, "shim"), capabilities);
        BrowserHostSource.AppendRawPlatformMembers(source, "", capabilities);

        Assert.Equal(["cli", "filesystem", "io"], capabilities.ShimFamilies.ToArray());
        Assert.True(capabilities.LinksFilesystem);
        var text = source.ToString();
        Assert.Contains("/preview2-filesystem-scope.mjs\";", text, StringComparison.Ordinal);
        Assert.Contains("createFilesystemScope: preopens => createPreview2FilesystemScope({", text, StringComparison.Ordinal);
        Assert.Contains("new preview2Filesystem.InMemoryFilesystemAdapter()", text, StringComparison.Ordinal);
        Assert.Contains("createShim: createSelectedPreview2ShimFactory({ cli: preview2Cli, filesystem: preview2Filesystem, io: preview2Io }),",
            text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequiredInterfaceWithoutAProviderOrShimFailsTheBuild()
    {
        using var directory = TemporaryDirectory.Create();
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting, "wasi:io/streams@0.2.11");

        var missing = Assert.Throws<ArgumentException>(() =>
            RawBrowserHostCapabilities.Select(hosting, ["wasi:random/random@0.2.11"]));
        Assert.Contains("'wasi:random/random@0.2.11' has no hosting platform provider", missing.Message, StringComparison.Ordinal);
        var unknown = Assert.Throws<ArgumentException>(() =>
            RawBrowserHostCapabilities.Select(hosting, ["wasi:gpu/device@0.1.0"]));
        Assert.Contains("'wasi:gpu/device@0.1.0' has no browser shim", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsACatalogWithWindowsLineEndingsAndRejectsAnUnexpectedFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var hosting = Path.Combine(directory.Path, "hosting");
        HostingCatalogFixture.Write(hosting);
        var catalogPath = Path.Combine(hosting, "platform-provider-catalog.generated.mjs");
        File.WriteAllText(catalogPath, File.ReadAllText(catalogPath).ReplaceLineEndings("\r\n"));

        Assert.Equal(["cli", "io"], RawBrowserHostCapabilities.Select(hosting, ["wasi:io/streams@0.2.11"]).ShimFamilies.ToArray());

        File.WriteAllText(catalogPath, "export const platformProviderCatalog = {};\n");
        Assert.Throws<InvalidOperationException>(() => RawBrowserHostCapabilities.Select(hosting, []));
    }
}
