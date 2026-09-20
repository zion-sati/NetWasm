using NetWasm.Hosting.Build.Environment;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Tests;

public sealed class HostToolsOverrideSelectorTests
{
    private static readonly ResolvedHostExecutable PackagedNode = new(
        HostToolIds.Node,
        Path.Combine(Path.GetTempPath(), "host-package", "tools", "bin", "node"),
        HostExecutableResolutionSource.Package);

    [Fact]
    public void ChooseUsesTheRestoredPackageWhenNoExplicitOverrideExists()
    {
        var environment = new RecordingEnvironment();
        var paths = new RecordingPaths();
        var chooser = new HostToolsOverrideSelector(environment, paths);

        var result = chooser.Choose(PackagedNode);

        Assert.Same(PackagedNode, result);
        Assert.Equal(["NETWASM_NODE_PATH"], environment.ReadNames);
        Assert.Empty(paths.Requests);
    }

    [Fact]
    public void ChoosePrefersASpecificOverrideToTheExplicitSdkRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "custom", "wasm-opt");
        var environment = new RecordingEnvironment(
            ("NETWASM_WASM_OPT_PATH", path),
            ("NETWASM_EMSDK_ROOT", Path.GetTempPath()));
        var paths = new RecordingPaths();
        var chooser = new HostToolsOverrideSelector(environment, paths);
        var packaged = PackagedNode with { ToolId = HostToolIds.BinaryenWasmOpt };

        var result = chooser.Choose(packaged);

        Assert.Equal(HostExecutableResolutionSource.Override, result.Source);
        Assert.Equal(["NETWASM_WASM_OPT_PATH"], environment.ReadNames);
        var request = Assert.Single(paths.Requests);
        Assert.Equal(HostToolIds.BinaryenWasmOpt, request.ToolId);
        Assert.False(request.SearchPath);
        Assert.Empty(request.RootFallbacks);
    }

    [Fact]
    public void ChooseUsesOnlyTheDeliberateSdkRootForNativeTools()
    {
        var environment = new RecordingEnvironment(
            ("NETWASM_EMSDK_ROOT", Path.GetTempPath()),
            ("EMSDK", Path.GetTempPath()));
        var paths = new RecordingPaths();
        var chooser = new HostToolsOverrideSelector(environment, paths);
        var packaged = PackagedNode with { ToolId = HostToolIds.WasmLd };

        var result = chooser.Choose(packaged);

        Assert.Equal(HostExecutableResolutionSource.Override, result.Source);
        Assert.Equal(["NETWASM_WASM_LD_PATH", "NETWASM_EMSDK_ROOT"],
            environment.ReadNames);
        var request = Assert.Single(paths.Requests);
        Assert.Single(request.RootFallbacks);
        Assert.Equal("NETWASM_EMSDK_ROOT", request.RootFallbacks[0].RootEnvironmentVariableName);
        Assert.False(request.SearchPath);
    }

    [Fact]
    public void ChooseRejectsAnEmptyDeliberateSdkRootInsteadOfFallingBack()
    {
        var environment = new RecordingEnvironment(("NETWASM_EMSDK_ROOT", ""));
        var paths = new RecordingPaths();
        var chooser = new HostToolsOverrideSelector(environment, paths);

        Assert.Throws<ArgumentException>(() => chooser.Choose(
            PackagedNode with { ToolId = HostToolIds.BinaryenWasmMerge }));

        Assert.Empty(paths.Requests);
    }

    private sealed class RecordingEnvironment(params (string Name, string Value)[] variables)
        : IHostEnvironmentVariableReader
    {
        internal List<string> ReadNames { get; } = [];

        public string? Read(string name)
        {
            ReadNames.Add(name);
            return variables.FirstOrDefault(item => item.Name == name).Value;
        }
    }

    private sealed class RecordingPaths : IHostExecutablePathResolver
    {
        internal List<HostExecutableResolutionRequest> Requests { get; } = [];

        public ResolvedHostExecutable Resolve(HostExecutableResolutionRequest request)
        {
            Requests.Add(request);
            return new(request.ToolId,
                Path.Combine(Path.GetTempPath(), "explicit", request.ExecutableName),
                HostExecutableResolutionSource.Override);
        }
    }
}
