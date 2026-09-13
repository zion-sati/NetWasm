namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentWorkspaceContractTests
{
    [Fact]
    public void PackageWorkspaceFactoryOwnsTemporaryDirectoryLifecycle()
    {
        using var files = new ComponentModelTestFiles();
        var factory = AsPackageFactory(new ComponentPackageWorkspaceFactory(
            new SystemDirectoryCreator(), new SystemDirectoryDeleter()));
        var workspace = factory.Create(files.PathFor("nested/component.wasm"));
        var temporary = workspace.TemporaryDirectory;

        Assert.True(Directory.Exists(temporary));
        Assert.EndsWith("linked.wasm", workspace.LinkedModulePath,
            StringComparison.Ordinal);
        workspace.Dispose();

        Assert.False(Directory.Exists(temporary));
    }

    [Fact]
    public void CoreModuleWorkspaceFactoryProducesDeterministicPaths()
    {
        var deletions = new RecordingDeleter();
        var factory = AsCoreFactory(new ComponentCoreModuleWorkspaceFactory(
            deletions));
        var workspace = factory.Create("component.wasm");

        Assert.Equal("component.wasm.environment.wasm", workspace.EnvironmentModulePath);
        Assert.Equal("component.wasm.netwasm-host.wasm", workspace.HostModulePath);
        Assert.Equal("component.wasm.managed-executable.wasm", workspace.ManagedExecutableAdapterModulePath);
        Assert.Equal("component.wasm.merged.wasm", workspace.MergedModulePath);
        Assert.Equal("component.wasm.sanitized.wasm", workspace.SanitizedModulePath);
        workspace.Dispose();
        Assert.Equal([
            "component.wasm.environment.wasm", "component.wasm.netwasm-host.wasm",
            "component.wasm.managed-executable.wasm", "component.wasm.merged.wasm",
            "component.wasm.sanitized.wasm",
        ], deletions.Paths);
    }

    private static IComponentPackageWorkspaceFactory AsPackageFactory(object value) =>
        (IComponentPackageWorkspaceFactory)value;

    private static IComponentCoreModuleWorkspaceFactory AsCoreFactory(object value) =>
        (IComponentCoreModuleWorkspaceFactory)value;

    private sealed class RecordingDeleter : IFileDeleter
    {
        public List<string> Paths { get; } = [];

        public void Delete(string path) => Paths.Add(path);
    }
}
