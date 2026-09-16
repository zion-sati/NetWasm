using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.Tests;

public sealed class LocalExecutionDescriptorBuildWriterTests
{
    [Fact]
    public void BindsDescriptorToLocalPathsAndPackageArchiveBytes()
    {
        using var directory = TemporaryDirectory.Create();
        var packageRoot = Path.Combine(directory.Path, "package");
        var archive = Path.Combine(directory.Path, "netwasm.toolchain.0.1.0-preview.24.nupkg");
        var manifest = Path.Combine(directory.Path, "deployment.json");
        var launcher = Path.Combine(directory.Path, "launcher.mjs");
        var node = Path.Combine(directory.Path, "node");
        var output = Path.Combine(directory.Path, "execution.json");
        Directory.CreateDirectory(packageRoot);
        File.WriteAllBytes(archive, [1, 2, 3]);
        File.WriteAllText(manifest, "{}");
        File.WriteAllText(launcher, "export {};");
        File.WriteAllText(node, "node");
        var hashes = new Sha256ContentHasher();
        var writer = new LocalExecutionDescriptorBuildWriter(
            new BuildArtifactStore(),
            hashes,
            new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));

        writer.Write(new(
            output,
            new string('a', 64),
            manifest,
            new string('b', 64),
            "0.2.0-preview.1",
            node,
            launcher,
            [new("NetWasm.Toolchain", "0.2.0-preview.24", packageRoot, archive)]));

        var descriptor = new ExecutionDescriptorReader(new ExecutionDescriptorValidator())
            .Read(File.ReadAllBytes(output));
        Assert.Equal(Path.GetFullPath(manifest), descriptor.DeploymentManifestPath);
        Assert.Equal(Path.GetFullPath(node), descriptor.HostExecutablePath);
        Assert.Equal(Path.GetFullPath(launcher), descriptor.LauncherPath);
        var package = Assert.Single(descriptor.ToolPackages);
        Assert.Equal(Path.GetFullPath(packageRoot), package.RootPath);
        Assert.Equal(hashes.Hash(File.ReadAllBytes(archive)), package.Sha256);
    }

    [Fact]
    public void RejectsMissingCapabilities()
    {
        var store = new BuildArtifactStore();
        var hashes = new Sha256ContentHasher();
        var descriptors = new ExecutionDescriptorWriter(new ExecutionDescriptorValidator());

        Assert.Throws<ArgumentNullException>(() =>
            new LocalExecutionDescriptorBuildWriter(null!, hashes, descriptors));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalExecutionDescriptorBuildWriter(store, null!, descriptors));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalExecutionDescriptorBuildWriter(store, hashes, null!));
    }
}
