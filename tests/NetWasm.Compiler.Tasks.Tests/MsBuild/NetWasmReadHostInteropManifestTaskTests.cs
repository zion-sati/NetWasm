using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmReadHostInteropManifestTaskTests
{
    [Fact]
    public void ProjectsJavaScriptImportsAndExportsForPublication()
    {
        var reader = new RecordingReader();
        var task = new NetWasmReadHostInteropManifestTask(reader)
        {
            ManifestPath = "interop.json",
            Target = "wasm64",
        };

        Assert.True(task.Execute());

        Assert.Equal("interop.json", reader.Path);
        Assert.Equal("wasm64", reader.Target);
        var import = Assert.Single(task.Imports);
        Assert.Equal("increment", import.ItemSpec);
        Assert.Equal("consumer.worker", import.GetMetadata("Module"));
        Assert.Equal("[\"i32\"]", import.GetMetadata("Parameters"));
        Assert.Equal("i32", import.GetMetadata("Result"));
        Assert.Equal(string.Empty, import.GetMetadata("AsyncReturn"));
        Assert.Collection(
            task.Exports,
            export =>
            {
                Assert.Equal("add", export.GetMetadata("Name"));
                Assert.Equal("[\"i32\",\"i32\"]", export.GetMetadata("Parameters"));
                Assert.Equal("i32", export.GetMetadata("Result"));
                Assert.Equal(string.Empty, export.GetMetadata("AsyncReturn"));
            },
            export =>
            {
                Assert.Equal("read", export.GetMetadata("Name"));
                Assert.Equal("[]", export.GetMetadata("Parameters"));
                Assert.Equal("i64", export.GetMetadata("Result"));
                Assert.Equal("task", export.GetMetadata("AsyncReturn"));
            });
    }

    [Fact]
    public void FailureClearsOutputsAndReportsStableDiagnostic()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmReadHostInteropManifestTask(new ThrowingReader())
        {
            BuildEngine = build,
            ManifestPath = "interop.json",
            Target = "wasm32",
        };

        Assert.False(task.Execute());

        Assert.Empty(task.Imports);
        Assert.Empty(task.Exports);
        Assert.Contains("NWSDK060", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilityAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() => new NetWasmReadHostInteropManifestTask(null!));
        Assert.NotNull(new NetWasmReadHostInteropManifestTask());
    }

    private sealed class RecordingReader : IHostInteropManifestReader
    {
        public string? Path { get; private set; }
        public string? Target { get; private set; }

        public HostInteropManifest Read(string path, string target)
        {
            Path = path;
            Target = target;
            return new(1, target, new(0, 1, 0), new(8, 8, 16, 8, 16),
            [
                new(RuntimeAbi.HostModule, "interop_string_length", ["i32"], "i32"),
                new("consumer.worker", "increment", ["i32"], "i32"),
            ],
            [
                new("add", ["i32", "i32"], "i32"),
                new("read", [], "i64") { AsyncReturn = "task" },
            ]);
        }
    }

    private sealed class ThrowingReader : IHostInteropManifestReader
    {
        public HostInteropManifest Read(string path, string target) =>
            throw new InvalidOperationException("invalid interop manifest");
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
