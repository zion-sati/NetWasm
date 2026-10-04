using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build.MsBuild;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmTranspileComponentTaskTests
{
    [Fact]
    public void WritesWitWorkerAdapterFromPersistedContract()
    {
        var transpiler = new RecordingTranspiler();
        var canonical = new RecordingCanonicalWriter();
        var worker = new RecordingWorkerWriter();
        var store = new RecordingStore();
        var contractPath = Path.GetFullPath("worker-contract.json");
        store.Files[contractPath] = "contract"u8.ToArray();
        var task = Create(transpiler, canonical, worker, store);
        task.WorkerContractPath = contractPath;

        Assert.True(task.Execute());

        Assert.Null(canonical.Request);
        Assert.Equal("contract", System.Text.Encoding.UTF8.GetString(
            worker.Request!.ContractJson.Span));
        Assert.Equal(CanonicalComponentAdapterWriter.SupportedJcoVersion, worker.Request.JcoVersion);
        Assert.Equal(
            [new WitWorkerRootExport("value", "function")],
            worker.Request.RootExports.ToArray());
        Assert.Equal("worker-adapter", System.Text.Encoding.UTF8.GetString(
            store.Files[Path.GetFullPath(task.AdapterPath)]));
        Assert.Collection(
            task.Artifacts,
            artifact => Assert.Equal("component-adapter", artifact.GetMetadata("Role")),
            artifact => Assert.Equal("component-javascript", artifact.GetMetadata("Role")),
            artifact => Assert.Equal("component-core-module", artifact.GetMetadata("Role")));
    }

    [Fact]
    public void PreservesCanonicalAdapterPath()
    {
        var canonical = new RecordingCanonicalWriter();
        var worker = new RecordingWorkerWriter();
        var store = new RecordingStore();
        var task = Create(new RecordingTranspiler(), canonical, worker, store);
        task.ExecutionContract = "wasi-command@0.2.11";

        Assert.True(task.Execute());

        Assert.Equal("wasi-command@0.2.11", canonical.Request?.ContractKey);
        Assert.Null(worker.Request);
        Assert.Equal("canonical-adapter", System.Text.Encoding.UTF8.GetString(
            store.Files[Path.GetFullPath(task.AdapterPath)]));
    }

    [Fact]
    public void ReportsFailureAndClearsArtifacts()
    {
        var build = new RecordingBuildEngine();
        var task = Create(
            new RecordingTranspiler { Failure = new InvalidOperationException("failed") },
            new RecordingCanonicalWriter(),
            new RecordingWorkerWriter(),
            new RecordingStore());
        task.BuildEngine = build;

        Assert.False(task.Execute());
        Assert.Empty(task.Artifacts);
        Assert.Contains("NWSDK045: failed", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRequiresEveryCapabilityAndComposesDefaults()
    {
        var transpiler = new RecordingTranspiler();
        var canonical = new RecordingCanonicalWriter();
        var worker = new RecordingWorkerWriter();
        var store = new RecordingStore();
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmTranspileComponentTask(null!, canonical, worker, store));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmTranspileComponentTask(transpiler, null!, worker, store));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmTranspileComponentTask(transpiler, canonical, null!, store));
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmTranspileComponentTask(transpiler, canonical, worker, null!));
        Assert.NotNull(new NetWasmTranspileComponentTask());
    }

    private static NetWasmTranspileComponentTask Create(
        IComponentTranspiler transpiler,
        ICanonicalComponentAdapterWriter canonical,
        IWitWorkerComponentAdapterWriter worker,
        IBuildArtifactStore store) => new(transpiler, canonical, worker, store)
        {
            NodePath = Path.GetFullPath("node"),
            JcoPath = Path.GetFullPath("jco"),
            JcoVersion = CanonicalComponentAdapterWriter.SupportedJcoVersion,
            ComponentPath = Path.GetFullPath("application.wasm"),
            OutputDirectory = Path.GetFullPath("component"),
            BaseName = "application-component",
            AdapterPath = Path.GetFullPath("application.adapter.mjs"),
            RelativeDirectory = "component",
        };

    private sealed class RecordingTranspiler : IComponentTranspiler
    {
        public Exception? Failure { get; init; }

        public ComponentTranspileResult Transpile(ComponentTranspileRequest request)
        {
            if (Failure is not null) throw Failure;
            return new(
                Path.Combine(request.OutputDirectory, request.BaseName + ".js"),
                [Path.Combine(request.OutputDirectory, request.BaseName + ".core.wasm")],
                [new("value", "function")]);
        }
    }

    private sealed class RecordingCanonicalWriter : ICanonicalComponentAdapterWriter
    {
        public CanonicalComponentAdapterRequest? Request { get; private set; }

        public byte[] Write(CanonicalComponentAdapterRequest request)
        {
            Request = request;
            return "canonical-adapter"u8.ToArray();
        }
    }

    private sealed class RecordingWorkerWriter : IWitWorkerComponentAdapterWriter
    {
        public WitWorkerComponentAdapterRequest? Request { get; private set; }

        public byte[] Write(WitWorkerComponentAdapterRequest request)
        {
            Request = request;
            return "worker-adapter"u8.ToArray();
        }
    }

    private sealed class RecordingStore : IBuildArtifactStore
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public byte[] Read(string path) => Files[path];

        public void Write(string path, ReadOnlySpan<byte> content) =>
            Files[path] = content.ToArray();

        public void Delete(string path) => Files.Remove(path);
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
