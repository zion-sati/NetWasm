using Microsoft.Build.Framework;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmReadRawBindingManifestTaskTests
{
    [Fact]
    public void ReadsPersistedCanonicalProviderInventory()
    {
        var path = Path.GetTempFileName();
        using var cleanup = new DeleteFile(path);
        File.WriteAllText(path, """
            {"schemaVersion":1,"target":"wasm32","requiredImports":[{"interface":"wasi:clocks/monotonic-clock@0.2.11","name":"now","parameters":[],"results":["u64"]}]}
            """);
        var task = new NetWasmReadRawBindingManifestTask
        {
            ManifestPath = path,
            Target = "wasm32",
        };

        Assert.True(task.Execute());

        var import = Assert.Single(task.RequiredImports);
        Assert.Equal("wasi:clocks/monotonic-clock@0.2.11", import.GetMetadata("Interface"));
        Assert.Equal("now", import.GetMetadata("Name"));
        Assert.Equal("[]", import.GetMetadata("Parameters"));
        Assert.Equal("[\"u64\"]", import.GetMetadata("Results"));
        Assert.Equal("wasi:clocks/monotonic-clock@0.2.11",
            Assert.Single(task.RequiredImportModules).ItemSpec);
    }

    [Fact]
    public void RejectsTargetDriftAndClearsOutputs()
    {
        var path = Path.GetTempFileName();
        using var cleanup = new DeleteFile(path);
        File.WriteAllText(path,
            "{\"schemaVersion\":1,\"target\":\"wasm64\",\"requiredImports\":[]}");
        var build = new RecordingBuildEngine();
        var task = new NetWasmReadRawBindingManifestTask
        {
            BuildEngine = build,
            ManifestPath = path,
            Target = "wasm32",
        };

        Assert.False(task.Execute());
        Assert.Empty(task.RequiredImports);
        Assert.Empty(task.RequiredImportModules);
        Assert.Contains("NWSDK063", Assert.Single(build.Errors), StringComparison.Ordinal);
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

    private sealed class DeleteFile(string path) : IDisposable
    {
        public void Dispose() => File.Delete(path);
    }
}
