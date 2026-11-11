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
        Assert.Equal(PathFor("native-wasm-tools"), task.NativeWasmToolsPath);
        Assert.Equal(PathFor("wasm-opt"), task.BinaryenWasmOptPath);
        Assert.Equal(PathFor("wasm-merge"), task.BinaryenWasmMergePath);
        Assert.Equal(PathFor("native-wasm-opt"), task.NativeBinaryenWasmOptPath);
        Assert.Equal("132.0", task.NativeBinaryenWasmOptVersion);
        Assert.Equal("native", task.BinaryenWasmOptImplementation);
        Assert.Equal("132.0.0", task.BinaryenPortableVersion);
        Assert.Equal(PathFor("binaryen.js"), task.BinaryenPath);
        Assert.Equal(PathFor("wasm-ld"), task.WasmLdPath);
        Assert.Equal("24.0.0", task.WasmLdVersion);
        Assert.Empty(task.CompilerPath);
        Assert.Equal("NetWasm.Toolchain", task.ToolchainPackageId);
        Assert.Equal("0.1.0-preview.29", task.ToolchainPackageVersion);
        Assert.Equal(PathFor("command.wit.wasm"), task.CommandWitPackagePath);
        Assert.Equal(PathFor("diagnostic-command.wit.wasm"),
            task.DiagnosticCommandWitPackagePath);
        Assert.Equal(PathFor("compiler.wit.wasm"), task.CompilerWitPackagePath);
        Assert.Equal(PathFor("inspect.mjs"), task.RawInspectionCommandPath);
        Assert.Equal(PathFor("jco.mjs"), task.JcoPath);
        Assert.Equal("1.41.0+netwasm.2", task.JcoVersion);
        Assert.Equal("0.13.0", task.Preview3ShimVersion);
        Assert.Equal(PathFor("jco"), task.NodeHttpProviderRoot);
        Assert.Equal(PathFor("http.js"), task.NodeHttpProviderEntryPath);
        Assert.Equal($"{PathFor("package.json")};{PathFor("client.js")}", task.NodeHttpProviderFiles);
        Assert.Equal(PathFor("jco-sockets"), task.NodeSocketsProviderRoot);
        Assert.Equal(PathFor("sockets.js"), task.NodeSocketsProviderEntryPath);
        Assert.Equal($"{PathFor("package.json")};{PathFor("tcp.js")}", task.NodeSocketsProviderFiles);
        Assert.Equal(PathFor("zoneinfo"), task.TimeZoneSourceRoot);
        Assert.Equal("2026c", task.TimeZoneDataVersion);
        Assert.Equal(PathFor("bundle.mjs"), task.HostingBundleCommandPath);
        Assert.Equal("1.2.4", task.RolldownVersion);
        Assert.Equal("1.256.0", task.WasmToolsVersion);
    }

    [Fact]
    public void ExecuteProjectsTheRestoredCompiler()
    {
        var task = new NetWasmResolveBuildEnvironmentTask(
            new RecordingResolver(CreateEnvironment() with { CompilerPath = PathFor("nwc") }))
        {
            ToolchainPackageRoot = Root,
        };

        Assert.True(task.Execute());

        Assert.Equal(PathFor("nwc"), task.CompilerPath);
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

    [Fact]
    public void ExecuteCachesResolvedOutputsAndReusesThemUnderTheSameStamp()
    {
        var cache = new MemoryCache();
        var first = new RecordingResolver(CreateEnvironment());
        var resolving = new NetWasmResolveBuildEnvironmentTask(first, new FixedStamper("stamp"), cache, cache)
        {
            ToolchainPackageRoot = Root,
            CachePath = "cache.json",
        };

        Assert.True(resolving.Execute());

        Assert.Equal(1, first.Calls);
        Assert.Equal(("cache.json", "stamp"), (cache.WrittenPath, cache.WrittenStamp));
        Assert.Equal(37, cache.Outputs!.Count);
        var second = new RecordingResolver(CreateEnvironment());
        var cached = new NetWasmResolveBuildEnvironmentTask(second, new FixedStamper("stamp"), cache, cache)
        {
            ToolchainPackageRoot = Root,
            CachePath = "cache.json",
        };

        Assert.True(cached.Execute());

        Assert.Equal(0, second.Calls);
        Assert.Equal(resolving.NodePath, cached.NodePath);
        Assert.Equal(resolving.RolldownVersion, cached.RolldownVersion);
        Assert.Equal(resolving.BinaryenWasmOptImplementation, cached.BinaryenWasmOptImplementation);
    }

    [Fact]
    public void ACacheDirectoryKeepsOutputsByStampForEveryProject()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-build-environment-").FullName;
        try
        {
            var cache = new BuildEnvironmentCacheReader();
            var writer = new BuildEnvironmentCacheWriter();
            NetWasmResolveBuildEnvironmentTask Task(RecordingResolver resolver, string stamp, string project) =>
                new(resolver, new FixedStamper(stamp), cache, writer)
                {
                    ToolchainPackageRoot = Root,
                    CachePath = Path.Combine(directory, project, "build-environment.json"),
                    CacheDirectory = Path.Combine(directory, "shared"),
                };
            var worker = new RecordingResolver(CreateEnvironment());
            var app = new RecordingResolver(CreateEnvironment());
            var otherEnvironment = new RecordingResolver(CreateEnvironment());

            Assert.True(Task(worker, "stamp", "worker").Execute());
            Assert.True(Task(app, "stamp", "app").Execute());
            Assert.True(Task(otherEnvironment, "other", "app").Execute());

            // The app reuses what the worker resolved; another environment resolves and keeps its own.
            Assert.Equal((1, 0, 1), (worker.Calls, app.Calls, otherEnvironment.Calls));
            Assert.Equal(["other.json", "stamp.json"],
                Directory.GetFiles(Path.Combine(directory, "shared")).Select(Path.GetFileName).Order());
            Assert.False(Directory.Exists(Path.Combine(directory, "worker")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExecuteResolvesWhenTheCachedOutputsAreIncomplete()
    {
        var cache = new MemoryCache { Outputs = new Dictionary<string, string> { ["NodePath"] = "stale" } };
        var resolver = new RecordingResolver(CreateEnvironment());
        var task = new NetWasmResolveBuildEnvironmentTask(resolver, new FixedStamper("stamp"), cache, cache)
        {
            ToolchainPackageRoot = Root,
            CachePath = "cache.json",
        };

        Assert.True(task.Execute());

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(PathFor("node"), task.NodePath);
    }

    [Fact]
    public void ExecuteWithoutACachePathNeitherStampsNorCaches()
    {
        var cache = new MemoryCache();
        var resolver = new RecordingResolver(CreateEnvironment());
        var task = new NetWasmResolveBuildEnvironmentTask(resolver, new ThrowingStamper(), cache, cache)
        {
            ToolchainPackageRoot = Root,
        };

        Assert.True(task.Execute());

        Assert.Equal(1, resolver.Calls);
        Assert.Null(cache.WrittenPath);
    }

    [Fact]
    public void ConstructorsRejectMissingCacheCollaborators()
    {
        var resolver = new RecordingResolver(CreateEnvironment());
        var cache = new MemoryCache();
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmResolveBuildEnvironmentTask(resolver, null!, cache, cache));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmResolveBuildEnvironmentTask(resolver, new FixedStamper("s"), null!, cache));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmResolveBuildEnvironmentTask(resolver, new FixedStamper("s"), cache, null!));
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
                new NativeBinaryenTool(
                    new(HostToolIds.BinaryenWasmOpt, PathFor("native-wasm-opt"),
                        HostExecutableResolutionSource.EnvironmentRoot),
                    new(HostToolIds.BinaryenWasmOpt, new Version(132, 0), []))),
            new(
                "NetWasm.Toolchain",
                "0.1.0-preview.29",
                PathFor("toolchain-manifest.json"),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    PathFor("command.wit.wasm"),
                    PathFor("diagnostic-command.wit.wasm"),
                    PathFor("compiler.wit.wasm")),
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
                    PathFor("wasm-merge"),
                    "132.0.0"),
                new(
                    "NetWasm.Toolchain",
                    "0.1.0-preview.29",
                    "1.41.0+netwasm.2",
                    "0.13.0",
                    node.AbsolutePath,
                    nodeCompatibility.Version,
                    PathFor("jco.mjs"),
                    PathFor("jco-lock"),
                    PathFor("jco-integrity"),
                    PathFor("jco-notices"),
                    PathFor("jco-policy"),
                    new(
                        PathFor("jco"),
                        PathFor("http.js"),
                        [PathFor("package.json"), PathFor("client.js")]),
                    new(
                        PathFor("jco-sockets"),
                        PathFor("sockets.js"),
                        [PathFor("package.json"), PathFor("tcp.js")])),
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
                    PathFor("bundle-policy")),
                new("NetWasm.Toolchain", "0.1.0-preview.29", PathFor("zoneinfo"), "2026c")))
        {
            NativeWasmTools = new(
                new(HostToolIds.WasmTools, PathFor("native-wasm-tools"), HostExecutableResolutionSource.Package),
                new(HostToolIds.WasmTools, new Version(1, 256, 0), [])),
        };
    }

    private static string PathFor(string name) => Path.Combine(Root, name);

    private sealed class RecordingResolver(HostingBuildEnvironment result) :
        IHostingBuildEnvironmentResolver
    {
        public HostingBuildEnvironmentRequest? Request { get; private set; }

        public int Calls { get; private set; }

        public HostingBuildEnvironment Resolve(HostingBuildEnvironmentRequest request)
        {
            Request = request;
            Calls++;
            return result;
        }
    }

    private sealed class FixedStamper(string stamp) : IBuildEnvironmentStamper
    {
        public string Stamp(HostingBuildEnvironmentRequest request) => stamp;
    }

    private sealed class ThrowingStamper : IBuildEnvironmentStamper
    {
        public string Stamp(HostingBuildEnvironmentRequest request) =>
            throw new InvalidOperationException("A task without a cache path must not stamp.");
    }

    private sealed class MemoryCache : IBuildEnvironmentCacheReader, IBuildEnvironmentCacheWriter
    {
        public IReadOnlyDictionary<string, string>? Outputs { get; set; }

        public string? WrittenPath { get; private set; }

        public string? WrittenStamp { get; private set; }

        public IReadOnlyDictionary<string, string>? Read(string path, string stamp) =>
            stamp == WrittenStamp || WrittenStamp is null ? Outputs : null;

        public void Write(string path, string stamp, IReadOnlyDictionary<string, string> outputs)
        {
            WrittenPath = path;
            WrittenStamp = stamp;
            Outputs = outputs;
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
