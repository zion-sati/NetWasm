using Microsoft.Build.Framework;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.MsBuild;

namespace NetWasm.Runtime.Pack.Tests.MsBuild;

public sealed class RuntimeMaterializationTaskTests
{
    [Fact]
    public void DefaultConstructorComposesMaterializationCapability() =>
        Assert.NotNull(new RuntimeMaterializationTask());

    [Fact]
    public void PublishesMaterializedRuntimeMetadata()
    {
        var materialization = new RuntimeMaterialization(
            "wasm64",
            "/output/runtime.wasm",
            "digest",
            "netwasm.runtime.v1",
            "build-seam",
            "fingerprint",
            65_552,
            179_904,
            262_144,
            8_589_934_592);
        var actor = new RecordingMaterializer(materialization);
        var build = new RecordingBuildEngine();
        var task = Create(actor, build);
        task.Target = "wasm64";
        task.InitialHeapSizeBytes = "131072";
        task.MaximumMemorySizeBytes = "8589934592";

        Assert.True(task.Execute());

        var module = Assert.Single(task.RuntimeModules);
        Assert.Equal(materialization.OutputPath, module.ItemSpec);
        Assert.Equal("RuntimeModule", module.GetMetadata("Kind"));
        Assert.Equal("wasm64", module.GetMetadata("WasmTarget"));
        Assert.Equal("65552", module.GetMetadata("RuntimeGlobalBase"));
        Assert.Equal("179904", module.GetMetadata("HeapBase"));
        Assert.Equal("262144", module.GetMetadata("InitialMemorySizeBytes"));
        Assert.Equal("8589934592", module.GetMetadata("MaximumMemorySizeBytes"));
        Assert.Equal("digest", module.GetMetadata("Digest"));
        Assert.Equal("netwasm.runtime.v1", module.GetMetadata("RuntimeAbi"));
        Assert.Equal("build-seam", module.GetMetadata("BuildSeam"));
        Assert.Equal("fingerprint", module.GetMetadata("ToolchainFingerprint"));
        Assert.Equal(131_072, actor.Request?.InitialHeapSizeBytes);
        Assert.Equal(8_589_934_592, actor.Request?.MaximumMemorySizeBytes);
        Assert.Empty(build.Errors);
    }

    [Fact]
    public void UsesManifestDefaultsWhenSizesAreEmpty()
    {
        var actor = new RecordingMaterializer(Materialization());
        var task = Create(actor, new RecordingBuildEngine());

        Assert.True(task.Execute());
        Assert.Null(actor.Request?.InitialHeapSizeBytes);
        Assert.Null(actor.Request?.MaximumMemorySizeBytes);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void ReportsInvalidSize(string value)
    {
        var build = new RecordingBuildEngine();
        var task = Create(new RecordingMaterializer(Materialization()), build);
        task.InitialHeapSizeBytes = value;

        Assert.False(task.Execute());
        Assert.Empty(task.RuntimeModules);
        Assert.Equal("NWPACK001: A NetWasm memory size property is invalid.", Assert.Single(build.Errors).Message);
    }

    [Fact]
    public void ReportsMaterializationFailure()
    {
        var build = new RecordingBuildEngine();
        var task = Create(new ThrowingMaterializer(), build);

        Assert.False(task.Execute());
        Assert.Empty(task.RuntimeModules);
        Assert.Equal("NWPACK001: materialization failed", Assert.Single(build.Errors).Message);
    }

    [Fact]
    public void ConstructorRejectsMissingCapability() =>
        Assert.Throws<ArgumentNullException>(() => new RuntimeMaterializationTask(null!));

    private static RuntimeMaterializationTask Create(
        IRuntimeModuleMaterializer materializer,
        IBuildEngine buildEngine) => new(materializer)
        {
            BuildEngine = buildEngine,
            ManifestPath = "/runtime/runtime-pack.json",
            RuntimeLayoutPath = "/output/runtime-layout.json",
            AssetRoot = "/runtime",
            WasmLdPath = "/tools/wasm-ld",
            WasmToolsNodePath = "/tools/node",
            WasmToolsCommandPath = "/tools/run-wasm-tools.mjs",
            WasmToolsModulePath = "/tools/wasm-tools.wasm",
            OutputPath = "/output/runtime.wasm",
            LogDirectory = "/output/logs",
            Target = "wasm32",
        };

    private static RuntimeMaterialization Materialization() => new(
        "wasm32",
        "/output/runtime.wasm",
        "digest",
        "abi",
        "build",
        "fingerprint",
        16,
        91_984,
        196_608,
        2_147_483_648);

    private sealed class RecordingMaterializer(RuntimeMaterialization result) : IRuntimeModuleMaterializer
    {
        public RuntimeMaterializationRequest? Request { get; private set; }

        public RuntimeMaterialization Materialize(RuntimeMaterializationRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class ThrowingMaterializer : IRuntimeModuleMaterializer
    {
        public RuntimeMaterialization Materialize(RuntimeMaterializationRequest request) =>
            throw new InvalidOperationException("materialization failed");
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public int ColumnNumberOfTaskNode => 0;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
