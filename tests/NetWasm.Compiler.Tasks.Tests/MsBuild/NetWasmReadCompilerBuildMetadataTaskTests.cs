using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.MsBuild;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmReadCompilerBuildMetadataTaskTests
{
    [Fact]
    public void ExposesOnlyTargetAndRuntimeFeatures()
    {
        var reader = new RecordingReader();
        var task = new NetWasmReadCompilerBuildMetadataTask(reader)
        {
            MetadataPath = "compiler.json",
        };

        Assert.True(task.Execute());

        Assert.Equal("compiler.json", reader.Path);
        Assert.Equal("wasm64", task.Target);
        Assert.Equal(["local-time"], task.RuntimeFeatures.Select(item => item.ItemSpec));
    }

    [Fact]
    public void FailureClearsOutputsAndReportsStableDiagnostic()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmReadCompilerBuildMetadataTask(new ThrowingReader())
        {
            BuildEngine = build,
            MetadataPath = "compiler.json",
        };

        Assert.False(task.Execute());

        Assert.Empty(task.Target);
        Assert.Empty(task.RuntimeFeatures);
        Assert.Contains("NWSDK033", Assert.Single(build.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilityAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmReadCompilerBuildMetadataTask(null!));
        Assert.NotNull(new NetWasmReadCompilerBuildMetadataTask());
    }

    private sealed class RecordingReader : ICompilerBuildMetadataReader
    {
        public string? Path { get; private set; }

        public CompilerBuildMetadata Read(string path)
        {
            Path = path;
            return new(1, "wasm64", ["local-time"],
                [new("host", "call", WasmFunctionType.Create(CliValueKind.Void))]);
        }
    }

    private sealed class ThrowingReader : ICompilerBuildMetadataReader
    {
        public CompilerBuildMetadata Read(string path) =>
            throw new InvalidOperationException("invalid metadata");
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
