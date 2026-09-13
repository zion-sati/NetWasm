using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Environment;
using NetWasm.Hosting.Build.MsBuild;
using NetWasm.Toolchain.Prerequisites;
using NetWasm.Toolchain.Resolution;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmResolveBuildEnvironmentTaskTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "netwasm-toolchain"));

    [Fact]
    public void ExecuteProjectsEveryResolvedToolchainProduct()
    {
        var resolver = new RecordingResolver(CreateEnvironment());
        var task = new NetWasmResolveBuildEnvironmentTask(resolver)
        {
            ToolchainPackageRoot = Root,
        };

        Assert.True(task.Execute());

        Assert.Equal(Root, resolver.Request?.ToolchainPackageRoot);
        Assert.Equal(PathFor("node"), task.NodePath);
        Assert.Equal("24.19.0", task.NodeVersion);
        Assert.Equal(PathFor("run-wasm-tools.mjs"), task.WasmToolsCommandPath);
        Assert.Equal(PathFor("wasm-tools.wasm"), task.WasmToolsModulePath);
        Assert.Equal(PathFor("wasm-opt"), task.BinaryenWasmOptPath);
        Assert.Equal(PathFor("wasm-merge"), task.BinaryenWasmMergePath);
        Assert.Equal(PathFor("binaryen.js"), task.BinaryenPath);
        Assert.Equal(PathFor("wasm-ld"), task.WasmLdPath);
        Assert.Equal("24.0.0", task.WasmLdVersion);
        Assert.Equal("NetWasm.Toolchain", task.ToolchainPackageId);
        Assert.Equal("0.1.0-preview.29", task.ToolchainPackageVersion);
        Assert.Equal(PathFor("command.wit.wasm"), task.CommandWitPackagePath);
        Assert.Equal(PathFor("async-command.wit.wasm"),
            task.AsyncCommandWitPackagePath);
        Assert.Equal(PathFor("compiler.wit.wasm"), task.CompilerWitPackagePath);
        Assert.Equal(PathFor("inspect.mjs"), task.RawInspectionCommandPath);
        Assert.Equal(PathFor("jco.mjs"), task.JcoPath);
        Assert.Equal("1.28.1", task.JcoVersion);
        Assert.Equal("0.24.1", task.Preview2ShimVersion);
        Assert.Equal(PathFor("preview2-shim"), task.Preview2ShimRoot);
        Assert.Equal(PathFor("bundle.mjs"), task.HostingBundleCommandPath);
        Assert.Equal("1.2.4", task.RolldownVersion);
        Assert.Equal("1.256.0", task.WasmToolsVersion);
    }

    [Fact]
    public void ExecuteReportsResolutionFailureWithoutPublishingOutputs()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmResolveBuildEnvironmentTask(
            new ThrowingResolver())
        {
            ToolchainPackageRoot = Root,
            BuildEngine = build,
        };

        Assert.False(task.Execute());

        Assert.Contains("NWSDK040", Assert.Single(build.Errors), StringComparison.Ordinal);
        Assert.Equal(string.Empty, task.BinaryenWasmOptPath);
        Assert.Equal(string.Empty, task.BinaryenWasmMergePath);
    }

    [Fact]
    public void ConstructorsRejectMissingResolverAndComposeDefault()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmResolveBuildEnvironmentTask(null!));
        Assert.NotNull(new NetWasmResolveBuildEnvironmentTask());
    }

    private static HostingBuildEnvironment CreateEnvironment()
    {
        var node = new ResolvedHostExecutable(
            HostToolIds.Node,
            PathFor("node"),
            HostExecutableResolutionSource.Path);
        var nodeCompatibility = new ValidatedHostToolCompatibility(
            HostToolIds.Node,
            new Version(24, 19, 0),
            []);
        return new(
            node,
            nodeCompatibility,
            new(
                HostToolIds.WasmLd,
                PathFor("wasm-ld"),
                HostExecutableResolutionSource.EnvironmentRoot),
            new(HostToolIds.WasmLd, new Version(24, 0, 0), []),
            new(
                "NetWasm.Toolchain",
                "0.1.0-preview.29",
                PathFor("toolchain-manifest.json"),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    PathFor("command.wit.wasm"),
                    PathFor("async-command.wit.wasm"),
                    PathFor("compiler.wit.wasm")),
                PathFor("preview2-shim"),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    "1.256.0",
                    node.AbsolutePath,
                    nodeCompatibility.Version,
                    PathFor("run-wasm-tools.mjs"),
                    PathFor("wasm-tools.wasm"),
                    PathFor("apache-license"),
                    PathFor("llvm-license"),
                    PathFor("mit-license"),
                    PathFor("wasm-tools-readme")),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    node.AbsolutePath,
                    nodeCompatibility.Version,
                    PathFor("inspect.mjs"),
                    PathFor("binaryen.js"),
                    PathFor("wasm-opt"),
                    PathFor("wasm-merge")),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    "1.28.1",
                    "0.24.1",
                    node.AbsolutePath,
                    nodeCompatibility.Version,
                    PathFor("jco.mjs"),
                    PathFor("jco-lock"),
                    PathFor("jco-integrity"),
                    PathFor("jco-notices"),
                    PathFor("jco-policy")),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    "1.2.4",
                    node.AbsolutePath,
                    nodeCompatibility.Version,
                    PathFor("bundle.mjs"),
                    PathFor("rolldown"),
                    PathFor("bundle-lock"),
                    PathFor("bundle-integrity"),
                    PathFor("bundle-notices"),
                    PathFor("bundle-policy"))));
    }

    private static string PathFor(string name) => Path.Combine(Root, name);

    private sealed class RecordingResolver(HostingBuildEnvironment result) :
        IHostingBuildEnvironmentResolver
    {
        public HostingBuildEnvironmentRequest? Request { get; private set; }

        public HostingBuildEnvironment Resolve(HostingBuildEnvironmentRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class ThrowingResolver : IHostingBuildEnvironmentResolver
    {
        public HostingBuildEnvironment Resolve(HostingBuildEnvironmentRequest request) =>
            throw new InvalidOperationException("resolution failed");
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) =>
            Errors.Add(e.Message ?? string.Empty);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;
    }
}
