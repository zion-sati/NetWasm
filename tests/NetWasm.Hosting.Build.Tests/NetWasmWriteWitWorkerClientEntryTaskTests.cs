using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmWriteWitWorkerClientEntryTaskTests
{
    [Fact]
    public void WritesTheGeneratedClientAndDeclarationFromTheExactContract()
    {
        var writer = new RecordingWriter();
        var store = new MemoryStore();
        var task = Create(writer, store);
        var contract = new byte[] { 1, 2, 3 };
        store.Files.Add(Path.GetFullPath(task.ContractPath), contract);
        Assert.True(task.Execute());
        var request = Assert.IsType<WitWorkerClientRequest>(writer.Request);
        Assert.Equal(contract, request.ContractJson.ToArray());
        Assert.Equal(Path.GetFullPath("hosting"), request.HostingJavaScriptRoot);
        Assert.Equal("worker.mjs", request.WorkerFileName);
        Assert.Equal(new string('a', 64), request.BuildFingerprint);
        Assert.Equal(new string('b', 64), request.ManifestSha256);
        Assert.Equal(new byte[] { 4 }, store.Files[Path.GetFullPath("client.mjs")]);
        Assert.Equal(new byte[] { 5 }, store.Files[Path.GetFullPath("client.d.mts")]);
    }

    [Fact]
    public void ReportsGenerationFailureWithoutWritingEitherArtifact()
    {
        var writer = new RecordingWriter();
        var store = new MemoryStore();
        var engine = new RecordingEngine();
        var task = Create(writer, store);
        task.BuildEngine = engine;
        Assert.False(task.Execute());
        Assert.Single(engine.Errors);
        Assert.Empty(store.Files);
        Assert.Null(writer.Request);
    }

    [Fact]
    public void RejectsMissingDependenciesAndComposesTheDefaultTask()
    {
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteWitWorkerClientEntryTask(null!, new MemoryStore()));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteWitWorkerClientEntryTask(new RecordingWriter(), null!));
        Assert.NotNull(new NetWasmWriteWitWorkerClientEntryTask());
    }

    private static NetWasmWriteWitWorkerClientEntryTask Create(RecordingWriter writer, MemoryStore store) => new(writer, store)
    {
        ContractPath = "contract.json",
        HostingJavaScriptRoot = "hosting",
        WorkerFileName = "worker.mjs",
        BuildFingerprint = new string('a', 64),
        ManifestSha256 = new string('b', 64),
        OutputPath = "client.mjs",
        DeclarationPath = "client.d.mts",
    };

    private sealed class RecordingWriter : IWitWorkerClientWriter
    {
        public WitWorkerClientRequest? Request { get; private set; }
        public WitWorkerClientSource Write(WitWorkerClientRequest request)
        {
            Request = request;
            return new([4], [5]);
        }
    }

    private sealed class MemoryStore : IBuildArtifactStore
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public byte[] Read(string path) => Files[path];
        public void Write(string path, ReadOnlySpan<byte> content) => Files.Add(path, content.ToArray());
        public void Delete(string path) => Files.Remove(path);
    }

    private sealed class RecordingEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => throw new NotSupportedException();
    }
}
