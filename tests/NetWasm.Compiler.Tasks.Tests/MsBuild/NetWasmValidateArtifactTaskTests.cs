using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmValidateArtifactTaskTests
{
    [Fact]
    public void ExecuteValidatesTheCurrentBuildRequest()
    {
        var builder = new RecordingRequestBuilder();
        var validator = new RecordingValidator();
        var task = new NetWasmValidateArtifactTask(builder, validator)
        {
            BuildEngine = new RecordingBuildEngine(),
            InputAssemblyPath = "app.dll",
            ArtifactManifestPath = "manifest.json",
            ProjectDirectory = ".",
        };

        Assert.True(task.Execute());
        Assert.NotNull(builder.Input);
        Assert.NotNull(validator.Request);
        Assert.Equal(task.ArtifactManifestPath, validator.Request.ManifestPath);
        Assert.Empty(task.ResolvedArtifacts);
    }

    [Fact]
    public void ExecuteReportsStaleArtifactsWithoutLeakingFailureDetails()
    {
        var build = new RecordingBuildEngine();
        var task = new NetWasmValidateArtifactTask(
            new RecordingRequestBuilder(),
            new ThrowingValidator())
        {
            BuildEngine = build,
            InputAssemblyPath = "app.dll",
            ArtifactManifestPath = "manifest.json",
            ProjectDirectory = ".",
        };

        Assert.False(task.Execute());
        var error = Assert.Single(build.Errors);
        Assert.Equal("NWSDK003: NetWasm artifact manifest is missing, invalid, or stale; rebuild the application.", error.Message);
    }

    [Fact]
    public void ConstructorRejectsMissingCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() => new NetWasmValidateArtifactTask(null!, new RecordingValidator()));
        Assert.Throws<ArgumentNullException>(() => new NetWasmValidateArtifactTask(new RecordingRequestBuilder(), null!));
    }

    [Fact]
    public void DefaultCompositionReportsAMissingManifestThroughTheTaskBoundary()
    {
        using var directory = new TemporaryDirectory();
        var build = new RecordingBuildEngine();
        var task = new NetWasmValidateArtifactTask
        {
            BuildEngine = build,
            InputAssemblyPath = directory.Write("app.dll", "assembly"),
            ArtifactManifestPath = Path.Combine(directory.Path, "missing.json"),
            ProjectDirectory = directory.Path,
        };

        Assert.False(task.Execute());
        Assert.Single(build.Errors);
    }

    private static CompilerArtifactManifestBuildResult Result() => new(
        new CompilerArtifactManifest(
            1,
            "build",
            "profile",
            "wasm32",
            "none",
            "sdk",
            "compiler",
            "abi",
            "runtime",
            ImmutableArray<CompilerArtifactManifestInput>.Empty,
            ImmutableArray<CompilerArtifactManifestArtifact>.Empty),
        ImmutableArray<CompilerArtifactResult>.Empty);

    private sealed class RecordingRequestBuilder : ICompilerArtifactManifestTaskRequestBuilder
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

    private sealed class RecordingValidator : ICompilerArtifactManifestValidator
    {
        public CompilerArtifactManifestValidationRequest? Request { get; private set; }

        public CompilerArtifactManifestBuildResult Validate(CompilerArtifactManifestValidationRequest request)
        {
            Request = request;
            return Result();
        }
    }

    private sealed class ThrowingValidator : ICompilerArtifactManifestValidator
    {
        public CompilerArtifactManifestBuildResult Validate(CompilerArtifactManifestValidationRequest request) =>
            throw new InvalidOperationException("sensitive implementation detail");
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-artifact-task-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
