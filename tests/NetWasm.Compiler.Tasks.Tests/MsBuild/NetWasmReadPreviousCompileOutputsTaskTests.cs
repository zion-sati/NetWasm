using System.Collections.Immutable;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class NetWasmReadPreviousCompileOutputsTaskTests
{
    [Fact]
    public void ExecuteRestoresTheDeclaredCallbackObjectAsAConditionalOutput()
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = directory.PathTo("manifest.json");
        var callbackPath = directory.PathTo("application.callbacks.o");
        WriteManifest(manifestPath, [Artifact("application.callbacks.o")]);
        var task = CreateTask(manifestPath, callbackPath);

        Assert.True(task.Execute());
        Assert.True(task.IsReadable);
        Assert.Equal(callbackPath, Assert.Single(task.ConditionalOutputs).ItemSpec);
    }

    [Fact]
    public void ExecuteLeavesNoncallbackAndMissingManifestsWithoutConditionalOutputs()
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = directory.PathTo("manifest.json");
        var task = CreateTask(
            manifestPath,
            directory.PathTo("application.callbacks.o"));

        Assert.True(task.Execute());
        Assert.True(task.IsReadable);
        Assert.Empty(task.ConditionalOutputs);

        WriteManifest(manifestPath, []);
        Assert.True(task.Execute());
        Assert.True(task.IsReadable);
        Assert.Empty(task.ConditionalOutputs);
    }

    [Fact]
    public void ExecuteMarksMalformedOrContradictoryManifestsUnreadable()
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = directory.Write("manifest.json", "not-json");
        var task = CreateTask(
            manifestPath,
            directory.PathTo("application.callbacks.o"));

        Assert.True(task.Execute());
        Assert.False(task.IsReadable);
        Assert.Empty(task.ConditionalOutputs);

        WriteManifest(manifestPath, [Artifact("other.callbacks.o")]);
        var contradictory = CreateTask(
            manifestPath,
            directory.PathTo("application.callbacks.o"));
        Assert.True(contradictory.Execute());
        Assert.False(contradictory.IsReadable);
        Assert.Empty(contradictory.ConditionalOutputs);
    }

    [Fact]
    public void ConstructorRejectsAMissingManifestReader()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NetWasmReadPreviousCompileOutputsTask(null!));
    }

    private static NetWasmReadPreviousCompileOutputsTask CreateTask(
        string manifestPath,
        string callbackPath) => new(new CompilerArtifactManifestReader())
        {
            ArtifactManifestPath = manifestPath,
            NativeCallbackObjectPath = callbackPath,
        };

    private static CompilerArtifactManifestArtifact Artifact(string path) => new(
        CompilerArtifactKinds.NativeCallbackSupportObject,
        path,
        "application/wasm",
        new string('0', 64),
        1,
        "wasm32",
        "netwasm0.1",
        "build");

    private static void WriteManifest(
        string path,
        ImmutableArray<CompilerArtifactManifestArtifact> artifacts) =>
        new CompilerArtifactManifestWriter().Write(new(
            path,
            new(
                1,
                "build",
                "netwasm0.1",
                "wasm32",
                "none",
                "sdk",
                "compiler",
                "abi",
                "runtime",
                [],
                artifacts)));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-compile-output-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathTo(string name) => System.IO.Path.Combine(Path, name);

        public string Write(string name, string content)
        {
            var path = PathTo(name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
