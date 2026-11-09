using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.Tests;

public sealed class LocalExecutionDescriptorBuildWriterTests
{
    [Fact]
    public void BindsDescriptorToLocalPaths()
    {
        using var directory = TemporaryDirectory.Create();
        var manifest = Path.Combine(directory.Path, "deployment.json");
        var launcher = Path.Combine(directory.Path, "launcher.mjs");
        var node = Path.Combine(directory.Path, "node");
        var output = Path.Combine(directory.Path, "execution.json");
        File.WriteAllText(manifest, "{}");
        File.WriteAllText(launcher, "export {};");
        File.WriteAllText(node, "node");
        var writer = new LocalExecutionDescriptorBuildWriter(
            new BuildArtifactStore(),
            new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));

        writer.Write(new(
            output,
            new string('a', 64),
            manifest,
            new string('b', 64),
            "0.1.0-preview.1",
            node,
            launcher));

        var descriptor = new ExecutionDescriptorReader(new ExecutionDescriptorValidator())
            .Read(File.ReadAllBytes(output));
        Assert.Equal(Path.GetFullPath(manifest), descriptor.DeploymentManifestPath);
        Assert.Equal(Path.GetFullPath(node), descriptor.HostExecutablePath);
        Assert.Equal(Path.GetFullPath(launcher), descriptor.LauncherPath);
    }

    [Fact]
    public void RejectsMissingCapabilities()
    {
        var store = new BuildArtifactStore();
        var descriptors = new ExecutionDescriptorWriter(new ExecutionDescriptorValidator());

        Assert.Throws<ArgumentNullException>(() =>
            new LocalExecutionDescriptorBuildWriter(null!, descriptors));
        Assert.Throws<ArgumentNullException>(() =>
            new LocalExecutionDescriptorBuildWriter(store, null!));
    }
}
