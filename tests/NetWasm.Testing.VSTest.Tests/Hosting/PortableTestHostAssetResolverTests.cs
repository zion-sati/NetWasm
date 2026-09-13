using System.Text.Json.Nodes;
using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest.Tests.Hosting;

public sealed class PortableTestHostAssetResolverTests
{
    [Fact]
    public void ResolvesTheIntegrityVerifiedPortableClosureFromTheExtensionRoot()
    {
        var extensionRoot = ProductExtensionRoot();

        var result = new PortableTestHostAssetResolver(extensionRoot).Resolve();

        Assert.Equal(Path.Combine(extensionRoot, "testhost", "testhost.dll"), result.TestHostPath);
        Assert.Equal(
            Path.Combine(extensionRoot, "testhost", "testhost.deps.json"),
            result.DependencyManifestPath);
        Assert.Equal(
            Path.Combine(extensionRoot, "testhost", "testhost.runtimeconfig.json"),
            result.RuntimeConfigurationPath);
        Assert.Equal(Path.Combine(extensionRoot, "testhost", "packages"), result.PackageProbingPath);
        Assert.All(
            [
                result.TestHostPath,
                result.DependencyManifestPath,
                result.RuntimeConfigurationPath,
            ],
            path => Assert.True(File.Exists(path)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("contains\0null")]
    public void RejectsInvalidExtensionRoots(string extensionRoot)
    {
        Assert.Throws<ArgumentException>(() => new PortableTestHostAssetResolver(extensionRoot));
    }

    [Fact]
    public void RejectsANonCanonicalExtensionRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "folder", "..", "extension");

        Assert.Throws<ArgumentException>(() => new PortableTestHostAssetResolver(root));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("null")]
    public void RejectsAMissingOrMalformedManifest(string scenario)
    {
        using var fixture = PortableClosureFixture.Create();
        var manifestPath = fixture.ManifestPath;
        if (scenario == "missing")
        {
            File.Delete(manifestPath);
        }
        else
        {
            File.WriteAllText(manifestPath, scenario == "null" ? "null" : "{");
        }

        Assert.Throws<InvalidDataException>(() =>
            new PortableTestHostAssetResolver(fixture.ExtensionRoot).Resolve());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("version")]
    [InlineData("empty-files")]
    [InlineData("null-file")]
    [InlineData("blank-path")]
    [InlineData("rooted-path")]
    [InlineData("backslash-path")]
    [InlineData("dot-segment")]
    [InlineData("null-hash")]
    [InlineData("short-hash")]
    [InlineData("uppercase-hash")]
    [InlineData("duplicate-path")]
    public void RejectsInvalidManifestIdentities(string scenario)
    {
        using var fixture = PortableClosureFixture.Create();
        var document = fixture.ReadManifest();
        var files = document["Files"]!.AsArray();
        switch (scenario)
        {
            case "schema":
                document["SchemaVersion"] = 2;
                break;
            case "version":
                document["TestPlatformVersion"] = "0.0.0";
                break;
            case "empty-files":
                document["Files"] = JsonNode.Parse("[]");
                break;
            case "null-file":
                files[0] = null;
                break;
            case "blank-path":
                files[0]!["RelativePath"] = "";
                break;
            case "rooted-path":
                files[0]!["RelativePath"] = Path.GetFullPath("outside.dll");
                break;
            case "backslash-path":
                files[0]!["RelativePath"] = "folder\\asset.dll";
                break;
            case "dot-segment":
                files[0]!["RelativePath"] = "folder/../asset.dll";
                break;
            case "null-hash":
                files[0]!["Sha256"] = null;
                break;
            case "short-hash":
                files[0]!["Sha256"] = "0";
                break;
            case "uppercase-hash":
                files[0]!["Sha256"] = files[0]!["Sha256"]!.GetValue<string>().ToUpperInvariant();
                break;
            case "duplicate-path":
                files.Add(files[0]!.DeepClone());
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unknown scenario {scenario}.");
        }
        fixture.WriteManifest(document);

        Assert.Throws<InvalidDataException>(() =>
            new PortableTestHostAssetResolver(fixture.ExtensionRoot).Resolve());
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("changed-file")]
    [InlineData("incomplete-set")]
    public void RejectsAnIncompleteOrChangedPortableClosure(string scenario)
    {
        using var fixture = PortableClosureFixture.Create();
        var document = fixture.ReadManifest();
        var files = document["Files"]!.AsArray();
        var firstRelativePath = files[0]!["RelativePath"]!.GetValue<string>();
        var firstPath = Path.Combine(
            fixture.TestHostRoot,
            firstRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (scenario == "missing-file")
        {
            File.Delete(firstPath);
        }
        else if (scenario == "changed-file")
        {
            File.AppendAllText(firstPath, "changed");
        }
        else
        {
            files.RemoveAt(files.Count - 1);
            fixture.WriteManifest(document);
        }

        Assert.Throws<InvalidDataException>(() =>
            new PortableTestHostAssetResolver(fixture.ExtensionRoot).Resolve());
    }

    private static string ProductExtensionRoot() =>
        Path.GetDirectoryName(typeof(NetWasmTestRuntimeProvider).Assembly.Location)!;
}

internal sealed class PortableClosureFixture : IDisposable
{
    private PortableClosureFixture(string extensionRoot)
    {
        ExtensionRoot = extensionRoot;
        TestHostRoot = Path.Combine(extensionRoot, "testhost");
        ManifestPath = Path.Combine(TestHostRoot, "testhost.manifest.json");
    }

    internal string ExtensionRoot { get; }

    internal string TestHostRoot { get; }

    internal string ManifestPath { get; }

    internal static PortableClosureFixture Create()
    {
        var extensionRoot = Path.Combine(
            Path.GetTempPath(),
            $"netwasm-vstest-{Guid.NewGuid():N}");
        var source = Path.Combine(
            Path.GetDirectoryName(typeof(NetWasmTestRuntimeProvider).Assembly.Location)!,
            "testhost");
        CopyDirectory(source, Path.Combine(extensionRoot, "testhost"));
        return new PortableClosureFixture(extensionRoot);
    }

    internal JsonObject ReadManifest() =>
        JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();

    internal void WriteManifest(JsonObject document) =>
        File.WriteAllText(ManifestPath, document.ToJsonString());

    public void Dispose() => Directory.Delete(ExtensionRoot, recursive: true);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
