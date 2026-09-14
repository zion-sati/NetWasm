using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace NetWasm.Toolchain.Tests;

public sealed class WitPackageArtifactTests
{
    [Fact]
    public void ToolchainManifestIdentityMatchesPackageIdentity()
    {
        var repository = FindRepositoryRoot();
        var toolchain = Path.Combine(repository, "src", "NetWasm.Toolchain");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            toolchain, "toolchain-manifest.json")));
        var project = XDocument.Load(Path.Combine(
            toolchain, "NetWasm.Toolchain.csproj"));

        Assert.Equal(
            project.Descendants("PackageId").Single().Value,
            manifest.RootElement.GetProperty("packageId").GetString());
        Assert.Equal(
            project.Descendants("Version").Single().Value,
            manifest.RootElement.GetProperty("packageVersion").GetString());
    }

    [Fact]
    public void CompiledProductsRemainBoundToSourcesAndPinnedGenerator()
    {
        var repository = FindRepositoryRoot();
        var toolchain = Path.Combine(repository, "src", "NetWasm.Toolchain");
        using var artifactManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            toolchain, "wit-packages", "wit-package-manifest.json")));
        using var toolchainManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            toolchain, "toolchain-manifest.json")));

        var generator = artifactManifest.RootElement.GetProperty("generator");
        Assert.Equal("1", artifactManifest.RootElement.GetProperty("schemaVersion")
            .GetString());
        Assert.Equal("wasm-tools.module", generator.GetProperty("assetId").GetString());
        var module = toolchainManifest.RootElement.GetProperty("assets")
            .EnumerateArray()
            .Single(asset => asset.GetProperty("id").GetString() == "wasm-tools.module");
        Assert.Equal(module.GetProperty("version").GetString(),
            generator.GetProperty("version").GetString());
        Assert.Equal(module.GetProperty("sha256").GetString(),
            generator.GetProperty("sha256").GetString());

        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["command"] = Path.Combine(toolchain, "wit", "wit-manifest.json"),
            ["async-command"] = Path.Combine(
                toolchain, "async-wit", "wit-manifest.json"),
            ["compiler"] = Path.Combine(
                repository, "wit", "netwasm-platform-1.0.0", "wit-manifest.json"),
        };
        var packagedSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["command"] = "../wit/wit-manifest.json",
            ["async-command"] = "../async-wit/wit-manifest.json",
            ["compiler"] = "../compiler-wit/wit-manifest.json",
        };
        var products = artifactManifest.RootElement.GetProperty("products")
            .EnumerateArray().ToArray();
        Assert.Equal(sources.Keys.Order(StringComparer.Ordinal),
            products.Select(product => product.GetProperty("name").GetString()!)
                .Order(StringComparer.Ordinal));
        foreach (var product in products)
        {
            var name = product.GetProperty("name").GetString()!;
            Assert.Equal(packagedSources[name],
                product.GetProperty("sourceManifest").GetString());
            Assert.Equal(Hash(sources[name]),
                product.GetProperty("sourceManifestSha256").GetString());
            Assert.Equal(
                Hash(Path.Combine(toolchain, "wit-packages",
                    product.GetProperty("file").GetString()!)),
                product.GetProperty("sha256").GetString());
        }

        var toolchainAssets = toolchainManifest.RootElement.GetProperty("assets")
            .EnumerateArray()
            .ToDictionary(
                asset => asset.GetProperty("id").GetString()!,
                StringComparer.Ordinal);
        var compiledArtifacts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wit.package-manifest"] = "wit-package-manifest.json",
            ["wit.command-package"] = "command.wit.wasm",
            ["wit.async-command-package"] = "async-command.wit.wasm",
            ["wit.compiler-package"] = "compiler.wit.wasm",
        };
        foreach (var (assetId, file) in compiledArtifacts)
        {
            Assert.Equal(
                Hash(Path.Combine(toolchain, "wit-packages", file)),
                toolchainAssets[assetId].GetProperty("sha256").GetString());
        }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The NetWasm repository root was not found.");
    }
}
