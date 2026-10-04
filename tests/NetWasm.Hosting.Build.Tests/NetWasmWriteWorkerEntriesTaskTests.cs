using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.JavaScript;
using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmWriteWorkerEntriesTaskTests
{
    [Fact]
    public void HostTaskBindsEveryRequiredModuleToOneSource()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWriteWorkerHostEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            Preview2ShimRoot = Path.GetFullPath("shim"),
            OutputPath = Path.GetFullPath("worker-entry.mjs"),
            Imports =
            [
                Item("increment", ("Module", "consumer.worker"), ("Name", "increment")),
                Item("read", ("Module", "consumer.worker"), ("Name", "read")),
            ],
            Modules = [Item("consumer.mjs", ("Module", "consumer.worker"))],
            Arguments = [new TaskItem("argument")],
            EnvironmentVariables = [Item("MODE", ("Value", "test"))],
            Clocks = [new TaskItem("monotonic")],
            Network = "denyAll",
            Randomness = false,
        };

        Assert.True(task.Execute());

        var request = Assert.IsType<WorkerHostEntryRequest>(writer.Host);
        var binding = Assert.Single(request.Imports);
        Assert.Equal("consumer.worker", binding.Module);
        Assert.Equal(Path.GetFullPath("consumer.mjs"), binding.SourcePath);
        Assert.Equal(["increment", "read"], binding.Functions.ToArray());
        Assert.Equal(["argument"], request.Arguments.ToArray());
        Assert.Equal(new WorkerEnvironmentVariable("MODE", "test"), Assert.Single(request.Environment));
        Assert.Equal(["monotonic"], request.Clocks.ToArray());
        Assert.Equal("denyAll", request.Network);
        Assert.False(request.Randomness);
    }

    [Fact]
    public void HostTaskRejectsMissingAndUnusedModuleSources()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWriteWorkerHostEntryTask(new RecordingWriter())
        {
            BuildEngine = build,
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            Preview2ShimRoot = Path.GetFullPath("shim"),
            OutputPath = Path.GetFullPath("worker-entry.mjs"),
            Imports = [Item("increment", ("Module", "consumer.worker"), ("Name", "increment"))],
            Network = "denyAll",
        };
        Assert.False(task.Execute());
        Assert.Contains("NWSDK061", Assert.Single(build.Errors), StringComparison.Ordinal);

        build.Errors.Clear();
        task.Imports = [];
        task.Modules = [Item("consumer.mjs", ("Module", "consumer.worker"))];
        Assert.False(task.Execute());
        Assert.Contains("unused", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void HostTaskFiltersOnlyUnreachableGeneratedClients()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWriteWorkerHostEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            Preview2ShimRoot = Path.GetFullPath("shim"),
            OutputPath = Path.GetFullPath("worker-entry.mjs"),
            Imports = [Item("create", ("Module", "used.worker"), ("Name", "create"))],
            Modules =
            [
                Item("used.mjs", ("Module", "used.worker"), ("NetWasmGeneratedWorkerClient", "true")),
                Item("unused.mjs", ("Module", "unused.worker"), ("NetWasmGeneratedWorkerClient", "true")),
            ],
            Network = "denyAll",
        };
        Assert.True(task.Execute());
        var binding = Assert.Single(Assert.IsType<WorkerHostEntryRequest>(writer.Host).Imports);
        Assert.Equal("used.worker", binding.Module);
        Assert.Equal(Path.GetFullPath("used.mjs"), binding.SourcePath);
        Assert.Equal(["create"], binding.Functions.ToArray());
    }

    [Fact]
    public void ClientTaskProjectsInteropMetadata()
    {
        var writer = new RecordingWriter();
        var task = new NetWasmWriteWorkerClientEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            WorkerFileName = "app.worker.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = Path.GetFullPath("client.mjs"),
            DeclarationPath = Path.GetFullPath("client.d.mts"),
            Exports =
            [
                Item("add", ("Name", "add"), ("Parameters", "[\"i32\",\"i32\"]"),
                    ("Result", "i32"), ("AsyncReturn", "task")),
            ],
        };

        Assert.True(task.Execute());

        var request = Assert.IsType<WorkerClientEntryRequest>(writer.Client);
        var export = Assert.Single(request.Exports);
        Assert.Equal("add", export.Name);
        Assert.Equal(["i32", "i32"], export.Parameters.ToArray());
        Assert.Equal("i32", export.Result);
        Assert.Equal("task", export.AsyncReturn);
    }

    [Fact]
    public void WitHostTaskProjectsBrowserCapabilities()
    {
        var writer = new RecordingWitWriter();
        var task = new NetWasmWriteWitWorkerHostEntryTask(writer)
        {
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            Preview2ShimRoot = Path.GetFullPath("shim"),
            OutputPath = Path.GetFullPath("worker-entry.mjs"),
            Arguments = [new TaskItem("argument")],
            ApplicationImports = [Item("events.mjs", ("Module", "example:app/events"),
                ("ArtifactPath", "imports/events.mjs"), ("FileHash", new string('A', 64)))],
            EnvironmentVariables = [Item("MODE", ("Value", "test"))],
            Clocks = [new TaskItem("wall")],
            Network = "denyAll",
            Randomness = false,
        };

        Assert.True(task.Execute());

        var request = Assert.IsType<WitWorkerHostEntryRequest>(writer.Request);
        Assert.Equal(["argument"], request.Arguments.ToArray());
        Assert.Equal(new WorkerEnvironmentVariable("MODE", "test"), Assert.Single(request.Environment));
        Assert.Equal(["wall"], request.Clocks.ToArray());
        Assert.Equal("denyAll", request.Network);
        Assert.False(request.Randomness);
        var binding = Assert.Single(request.ApplicationImports);
        Assert.Equal("example:app/events", binding.Module);
        Assert.Equal("imports/events.mjs", binding.ArtifactPath);
        Assert.Equal(new string('a', 64), binding.Sha256);
    }

    [Fact]
    public void WitHostTaskReportsWriterFailure()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWriteWitWorkerHostEntryTask(new FailingWitWriter())
        {
            BuildEngine = build,
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            Preview2ShimRoot = Path.GetFullPath("shim"),
            OutputPath = Path.GetFullPath("worker-entry.mjs"),
            Network = "denyAll",
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK066", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ClientTaskReportsMalformedMetadata()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmWriteWorkerClientEntryTask(new RecordingWriter())
        {
            BuildEngine = build,
            HostingJavaScriptRoot = Path.GetFullPath("hosting"),
            WorkerFileName = "app.worker.mjs",
            BuildFingerprint = new string('1', 64),
            ManifestSha256 = new string('2', 64),
            OutputPath = Path.GetFullPath("client.mjs"),
            DeclarationPath = Path.GetFullPath("client.d.mts"),
            Exports = [Item("add", ("Name", "add"), ("Parameters", "invalid"), ("Result", "i32"))],
        };

        Assert.False(task.Execute());
        Assert.Contains("NWSDK062", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorsRejectMissingWriterAndComposeDefaults()
    {
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteWorkerHostEntryTask(null!));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteWitWorkerHostEntryTask(null!));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteWorkerClientEntryTask(null!));
        Assert.NotNull(new NetWasmWriteWorkerHostEntryTask());
        Assert.NotNull(new NetWasmWriteWitWorkerHostEntryTask());
        Assert.NotNull(new NetWasmWriteWorkerClientEntryTask());
    }

    private static TaskItem Item(string include, params (string Name, string Value)[] metadata)
    {
        var item = new TaskItem(include);
        foreach (var (name, value) in metadata) item.SetMetadata(name, value);
        return item;
    }

    private sealed class RecordingWriter : IWorkerEntryWriter
    {
        public WorkerHostEntryRequest? Host { get; private set; }
        public WorkerClientEntryRequest? Client { get; private set; }
        public void WriteHost(WorkerHostEntryRequest request) => Host = request;
        public void WriteClient(WorkerClientEntryRequest request) => Client = request;
    }

    private sealed class RecordingWitWriter : IWitWorkerHostEntryWriter
    {
        public WitWorkerHostEntryRequest? Request { get; private set; }
        public void Write(WitWorkerHostEntryRequest request) => Request = request;
    }

    private sealed class FailingWitWriter : IWitWorkerHostEntryWriter
    {
        public void Write(WitWorkerHostEntryRequest request) =>
            throw new InvalidOperationException("failed");
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);
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
