using Microsoft.Build.Framework;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmMaterializeWitWorkerPackageTaskTests
{
    [Fact]
    public void ReportsResolvedPathAndFailures()
    {
        var materializer = new RecordingMaterializer { Result = "/resolved" };
        var task = new NetWasmMaterializeWitWorkerPackageTask(materializer)
        {
            AuthoredWitPath = "authored",
            PlatformWitPackagePath = "platform",
            OutputDirectory = "output",
        };

        Assert.True(task.Execute());
        Assert.Equal("/resolved", task.ResolvedWitPath);
        Assert.Equal("authored", materializer.Request?.AuthoredPath);

        var build = new RecordingBuildEngine();
        materializer.Failure = new InvalidOperationException("failed");
        task.BuildEngine = build;
        Assert.False(task.Execute());
        Assert.Empty(task.ResolvedWitPath);
        Assert.Contains("NWSDK071: failed", Assert.Single(build.Errors));
    }

    [Fact]
    public void RejectsMissingActorAndComposesDefaults()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmMaterializeWitWorkerPackageTask(null!));
        Assert.NotNull(new NetWasmMaterializeWitWorkerPackageTask());
    }

    private sealed class RecordingMaterializer : IWitWorkerPackageMaterializer
    {
        public string Result { get; init; } = string.Empty;
        public Exception? Failure { get; set; }
        public WitWorkerPackageMaterializationRequest? Request { get; private set; }

        public string Materialize(WitWorkerPackageMaterializationRequest request)
        {
            Request = request;
            if (Failure is not null)
            {
                throw Failure;
            }
            return Result;
        }
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
