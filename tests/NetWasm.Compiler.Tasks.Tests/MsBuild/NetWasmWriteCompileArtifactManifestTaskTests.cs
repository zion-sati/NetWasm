using Microsoft.Build.Framework;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmWriteCompileArtifactManifestTaskTests
{
    [Fact]
    public void DefaultConstructorComposesTheProductionCapabilities() =>
        Assert.IsAssignableFrom<ITask>(new NetWasmWriteCompileArtifactManifestTask());

    [Fact]
    public void WritesTheManifestAndPublishesResolvedMetadata()
    {
        var manifest = new CompilerArtifactManifest(
            1, "build-id", "netwasm0.1", "wasm64", "none", "sdk", "compiler", "abi", "runtime", [],
            [new("CoreModule", "core.wasm", "application/wasm", "digest", 1, "wasm64", "netwasm0.1", "build-id")]);
        var result = new CompilerArtifactManifestBuildResult(
            manifest,
            [new("application.core.wasm", manifest.Artifacts[0])]);
        var requests = new RecordingManifestRequestBuilder();
        var writer = new RecordingManifestWriter();
        var task = CreateTask(requests, new RecordingManifestBuilder(result), writer);
        task.NativeCallbackObjectPath = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.o");

        Assert.True(task.Execute());

        Assert.Empty(requests.Input!.Artifacts);
        Assert.Same(manifest, writer.Request!.Manifest);
        Assert.Equal(task.ArtifactManifestPath, writer.Request.Path);
        Assert.Equal("build-id", task.SemanticBuildId);
        var resolved = Assert.Single(task.ResolvedArtifacts);
        Assert.Equal("CoreModule", resolved.GetMetadata("Kind"));
        Assert.Equal("application.core.wasm", resolved.GetMetadata("RelativePath"));
        Assert.Equal("core.wasm", resolved.GetMetadata("ManifestPath"));
        Assert.Equal("digest", resolved.GetMetadata("Digest"));
        Assert.Equal("PreserveNewest", resolved.GetMetadata("CopyToPublishDirectory"));
    }

    [Fact]
    public void RecordsTheCallbackObjectNwcWroteAsAnInternalArtifact()
    {
        var callbacks = Path.Combine(Path.GetTempPath(), $"netwasm-callbacks-{Guid.NewGuid():N}.o");
        File.WriteAllBytes(callbacks, [0]);
        try
        {
            var requests = new RecordingManifestRequestBuilder();
            var task = CreateTask(requests, new RecordingManifestBuilder(EmptyResult()), new RecordingManifestWriter());
            task.NativeCallbackObjectPath = callbacks;

            Assert.True(task.Execute());

            var artifact = Assert.Single(requests.Input!.Artifacts);
            Assert.Equal(callbacks, artifact.ItemSpec);
            Assert.Equal("NativeCallbackSupportObject", artifact.GetMetadata("Kind"));
            Assert.Equal("application/wasm", artifact.GetMetadata("MediaType"));
            Assert.Equal("Never", artifact.GetMetadata("CopyToPublishDirectory"));
        }
        finally
        {
            File.Delete(callbacks);
        }
    }

    [Fact]
    public void LeavesOutAnUnnamedCallbackObject()
    {
        var requests = new RecordingManifestRequestBuilder();
        var task = CreateTask(requests, new RecordingManifestBuilder(EmptyResult()), new RecordingManifestWriter());

        Assert.True(task.Execute());

        Assert.Empty(requests.Input!.Artifacts);
    }

    [Fact]
    public void ReportsAFailureWithoutAStackTrace()
    {
        var build = new RecordingBuildEngine();
        var task = CreateTask(
            new RecordingManifestRequestBuilder(), new ThrowingManifestBuilder(), new RecordingManifestWriter());
        task.BuildEngine = build;

        Assert.False(task.Execute());

        Assert.Equal("manifest failure", Assert.Single(build.Errors).Message);
    }

    [Fact]
    public void RejectsMissingCapabilities()
    {
        var requests = new RecordingManifestRequestBuilder();
        var builder = new RecordingManifestBuilder(EmptyResult());
        var writer = new RecordingManifestWriter();

        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteCompileArtifactManifestTask(null!, builder, writer));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteCompileArtifactManifestTask(requests, null!, writer));
        Assert.Throws<ArgumentNullException>(() => new NetWasmWriteCompileArtifactManifestTask(requests, builder, null!));
    }

    private static NetWasmWriteCompileArtifactManifestTask CreateTask(
        ICompilerArtifactManifestTaskRequestBuilder requests,
        ICompilerArtifactManifestBuilder builder,
        ICompilerArtifactManifestWriter writer) => new(requests, builder, writer)
        {
            InputAssemblyPath = "application.dll",
            ArtifactManifestPath = "manifest.json",
            ProjectDirectory = ".",
        };

    private static CompilerArtifactManifestBuildResult EmptyResult() => new(
        new(1, "build", "profile", "wasm32", "none", "sdk", "compiler", "abi", "runtime", [], []),
        []);

    private sealed class RecordingManifestRequestBuilder : ICompilerArtifactManifestTaskRequestBuilder
    {
        public CompilerArtifactManifestTaskInput? Input { get; private set; }

        public CompilerArtifactManifestBuildRequest Build(CompilerArtifactManifestTaskInput input)
        {
            Input = input;
            return new(
                input.ManifestPath,
                input.ProjectDirectory,
                input.Profile,
                input.Target,
                input.FeatureSet,
                input.SdkVersion,
                input.CompilerVersion,
                input.RuntimeAbiVersion,
                input.RuntimeVersion,
                [],
                []);
        }
    }

    private sealed class RecordingManifestBuilder(CompilerArtifactManifestBuildResult result) :
        ICompilerArtifactManifestBuilder
    {
        public CompilerArtifactManifestBuildResult Build(CompilerArtifactManifestBuildRequest request) => result;
    }

    private sealed class ThrowingManifestBuilder : ICompilerArtifactManifestBuilder
    {
        public CompilerArtifactManifestBuildResult Build(CompilerArtifactManifestBuildRequest request) =>
            throw new InvalidOperationException("manifest failure");
    }

    private sealed class RecordingManifestWriter : ICompilerArtifactManifestWriter
    {
        public CompilerArtifactManifestWriteRequest? Request { get; private set; }

        public void Write(CompilerArtifactManifestWriteRequest request) => Request = request;
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
