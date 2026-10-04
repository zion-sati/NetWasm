using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeLinkWorkspaceFactoryTests
{
    [Fact]
    public void CreatesIndependentWorkspacesAndReleasesOnlyOwnedContents()
    {
        using var directory = new TemporaryDirectory();
        var sibling = directory.Write("keep.log", "retained log");
        var factory = Assert.IsAssignableFrom<IRuntimeNativeLinkWorkspaceFactory>(new RuntimeNativeLinkWorkspaceFactory());
        var first = factory.Create(directory.Path);
        var second = factory.Create(directory.Path);
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        Assert.NotEqual(first.LogDirectoryPath, second.LogDirectoryPath);
        var proof = Path.Combine(first.LogDirectoryPath, "native-probe-link.log");
        File.WriteAllText(proof, "retained proof");
        Assert.True(Directory.Exists(first.DirectoryPath));
        File.WriteAllText(Path.Combine(first.DirectoryPath, "runtime.wasm"), "disposable artifact");

        first.Dispose();
        first.Dispose();

        Assert.False(Directory.Exists(first.DirectoryPath));
        Assert.True(Directory.Exists(second.DirectoryPath));
        Assert.True(File.Exists(sibling));
        Assert.True(File.Exists(proof));
        second.Dispose();
        Assert.False(Directory.Exists(second.DirectoryPath));
    }

    [Fact]
    public void RejectsMissingRootBeforeCreatingAWorkspace() =>
        Assert.Throws<ArgumentException>(() => new RuntimeNativeLinkWorkspaceFactory().Create(" "));
}
