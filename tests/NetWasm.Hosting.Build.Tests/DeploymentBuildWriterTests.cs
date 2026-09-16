using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.Tests;

public sealed class DeploymentBuildWriterTests
{
    [Fact]
    public void WritesArtifactsBeforeAValidatedDeterministicManifest()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "input", "application.wasm");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, [0, 97, 115, 109]);
        var deploymentRoot = Path.Combine(directory.Path, "deployment");
        var manifestPath = Path.Combine(deploymentRoot, "deployment.json");
        var writer = Create();
        var request = Request(source, deploymentRoot, manifestPath);

        var first = writer.Write(request);
        var firstBytes = File.ReadAllBytes(manifestPath);
        var second = writer.Write(request);

        Assert.Equal(first.Manifest.BuildFingerprint, second.Manifest.BuildFingerprint);
        Assert.Equal(first.ManifestSha256, second.ManifestSha256);
        Assert.Equal(firstBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal([0, 97, 115, 109], File.ReadAllBytes(
            Path.Combine(deploymentRoot, "application.wasm")));
        var manifest = new DeploymentManifestReader(new DeploymentManifestValidator())
            .Read(firstBytes);
        Assert.Equal(new string('a', 64), manifest.SemanticBuildId);
        Assert.Equal(DeploymentKind.Component, manifest.DeploymentKind);
        Assert.Equal<string>(["local-time"], manifest.RuntimeFeatures);
        Assert.Equal(first.Manifest.BuildFingerprint, manifest.BuildFingerprint);
        Assert.Equal(first.ManifestSha256, new Sha256ContentHasher().Hash(firstBytes));
        var artifact = Assert.Single(manifest.Artifacts);
        Assert.Equal("application.wasm", artifact.RelativePath);
        Assert.Equal("application", artifact.Role);
    }

    [Fact]
    public void RemovesPreviouslyAcceptedManifestBeforeArtifactReadFailure()
    {
        using var directory = TemporaryDirectory.Create();
        var deploymentRoot = Path.Combine(directory.Path, "deployment");
        var manifestPath = Path.Combine(deploymentRoot, "deployment.json");
        Directory.CreateDirectory(deploymentRoot);
        File.WriteAllText(manifestPath, "stale");

        Assert.Throws<FileNotFoundException>(() => Create().Write(Request(
            Path.Combine(directory.Path, "missing.wasm"),
            deploymentRoot,
            manifestPath)));

        Assert.False(File.Exists(manifestPath));
    }

    [Fact]
    public void RejectsMissingCapabilities()
    {
        var store = new BuildArtifactStore();
        var hashes = new Sha256ContentHasher();
        var manifests = new DeploymentManifestWriter(new DeploymentManifestValidator());

        Assert.Throws<ArgumentNullException>(() => new DeploymentBuildWriter(null!, hashes, manifests));
        Assert.Throws<ArgumentNullException>(() => new DeploymentBuildWriter(store, null!, manifests));
        Assert.Throws<ArgumentNullException>(() => new DeploymentBuildWriter(store, hashes, null!));
    }

    private static DeploymentBuildWriter Create() => new(
        new BuildArtifactStore(),
        new Sha256ContentHasher(),
        new DeploymentManifestWriter(new DeploymentManifestValidator()));

    private static DeploymentBuildRequest Request(
        string source,
        string root,
        string manifest) => new(
            manifest,
            root,
            new string('a', 64),
            DeploymentKind.Component,
            "netwasm0.1",
            "wasm32",
            "default",
            "wasi-command@0.2.11",
            new("0.2.0-preview.60", "0.2.0-preview.1", "0.2.0-preview.16",
                "netwasm.runtime.v1", "0.2.0-preview.1", "0.2.0-preview.24"),
            ["local-time"],
            [new(source, "application.wasm", "application", "application/wasm", null)],
            ["wasi:cli/environment@0.2.11"],
            [new("wasi:cli/environment@0.2.11", "get-environment", [], ["list<tuple<string,string>>"])],
            [new("wasi:cli/run@0.2.11", "run", [], ["result<unit,unit>"])]);
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-hosting-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new(path);
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
