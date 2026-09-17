using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeModuleMaterializerTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void MaterializesAndValidatesTargetSpecificRuntime(string target)
    {
        using var directory = new TemporaryDirectory();
        var manifest = RuntimePackTestData.Manifest();
        var sourceLayout = new RuntimeLayout(2, target, 65_537);
        var calculatedLayout = RuntimePackTestData.Layout(target);
        var manifests = new RecordingManifestReader(manifest);
        var layouts = new RecordingLayoutReader(sourceLayout);
        var calculator = new RecordingLayoutCalculator(calculatedLayout);
        var assets = new RecordingAssetVerifier();
        var arguments = new RecordingArgumentBuilder(["link"]);
        var commands = new RecordingCommandInvoker();
        var materializer = new RuntimeModuleMaterializer(
            manifests,
            layouts,
            calculator,
            assets,
            arguments,
            commands,
            new ConstantDigestCalculator("output-digest"));
        var request = Request(directory, target) with
        {
            InitialHeapSizeBytes = 131_072,
            MaximumMemorySizeBytes = calculatedLayout.MaximumMemorySizeBytes,
        };

        var capability = Assert.IsAssignableFrom<IRuntimeModuleMaterializer>(materializer);
        var result = capability.Materialize(request);

        Assert.Equal(target, result.Target);
        Assert.Equal(Path.GetFullPath(request.OutputPath), result.OutputPath);
        Assert.Equal("output-digest", result.Sha256);
        Assert.Equal(manifest.RuntimeAbi, result.RuntimeAbi);
        Assert.Equal(manifest.Provenance.BuildSeam, result.BuildSeam);
        Assert.Equal(manifest.Provenance.ToolchainFingerprint, result.ToolchainFingerprint);
        Assert.Equal(calculatedLayout.RuntimeGlobalBase, result.RuntimeGlobalBase);
        Assert.Equal(calculatedLayout.HeapBase, result.HeapBase);
        Assert.Equal(calculatedLayout.InitialMemorySizeBytes, result.InitialMemorySizeBytes);
        Assert.Equal(calculatedLayout.MaximumMemorySizeBytes, result.MaximumMemorySizeBytes);
        Assert.Equal(request.ManifestPath, manifests.Path);
        Assert.Equal(request.RuntimeLayoutPath, layouts.Path);
        Assert.Equal(request.InitialHeapSizeBytes, calculator.Request?.InitialHeapSizeBytes);
        Assert.Equal(request.MaximumMemorySizeBytes, calculator.Request?.MaximumMemorySizeBytes);
        Assert.Single(assets.Verifications);
        Assert.Equal(request.OutputPath, arguments.Request?.OutputPath);
        Assert.Equal(2, commands.Commands.Count);
        var link = commands.Commands[0];
        Assert.Equal(request.WasmLdPath, link.ExecutablePath);
        Assert.Equal(["link"], link.Arguments.ToArray());
        Assert.EndsWith("runtime-link.log", link.LogPath, StringComparison.Ordinal);
        var validation = commands.Commands[1];
        Assert.Equal(request.WasmToolsNodePath, validation.ExecutablePath);
        Assert.Equal("--disable-warning=ExperimentalWarning", validation.Arguments[0]);
        Assert.Equal(request.WasmToolsCommandPath, validation.Arguments[1]);
        Assert.Equal(request.WasmToolsModulePath, validation.Arguments[2]);
        Assert.Equal("validate", validation.Arguments[3]);
        Assert.Equal(Path.GetFullPath(request.OutputPath), validation.Arguments[4]);
        Assert.EndsWith("runtime-validate.log", validation.LogPath, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLayoutTargetMismatch()
    {
        using var directory = new TemporaryDirectory();
        var materializer = Create(new RuntimeLayout(2, "wasm64", 0));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Request(directory, "wasm32")));
        Assert.Equal("The NetWasm runtime layout target does not match the requested target.", exception.Message);
    }

    [Fact]
    public void RejectsUnavailableTarget()
    {
        using var directory = new TemporaryDirectory();
        var materializer = Create(new RuntimeLayout(2, "wasm128", 0));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            materializer.Materialize(Request(directory, "wasm128")));
        Assert.Equal("The requested NetWasm runtime target is unavailable.", exception.Message);
    }

    [Fact]
    public void RejectsMissingRequest()
    {
        using var directory = new TemporaryDirectory();
        var materializer = Create(new RuntimeLayout(2, "wasm32", 0));
        Assert.Throws<ArgumentNullException>(() => materializer.Materialize(null!));
        Assert.Throws<ArgumentException>(() => materializer.Materialize(
            Request(directory, "wasm32") with { ManifestPath = "" }));
    }

    [Fact]
    public void RejectsMismatchedEmscriptenVersion()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32");
        directory.Write("upstream/emscripten/emscripten-version.txt", "99.0.0\n");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Create(new RuntimeLayout(2, "wasm32", 0)).Materialize(request));

        Assert.Contains("requires activated Emscripten 6.0.7", exception.Message);
    }

    [Fact]
    public void RejectsMissingInstalledEmscriptenSystemLibrary()
    {
        using var directory = new TemporaryDirectory();
        var request = Request(directory, "wasm32") with
        {
            EmscriptenCacheRoot = directory.PathTo("empty-cache"),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Create(new RuntimeLayout(2, "wasm32", 0)).Materialize(request));

        Assert.Contains("required Emscripten system library 'libc.a' is unavailable", exception.Message);
    }

    private static RuntimeModuleMaterializer Create(RuntimeLayout layout) => new(
        new RecordingManifestReader(RuntimePackTestData.Manifest()),
        new RecordingLayoutReader(layout),
        new RecordingLayoutCalculator(RuntimePackTestData.Layout()),
        new RecordingAssetVerifier(),
        new RecordingArgumentBuilder(["link"]),
        new RecordingCommandInvoker(),
        new ConstantDigestCalculator("digest"));

    private static RuntimeMaterializationRequest Request(TemporaryDirectory directory, string target)
    {
        directory.Write("upstream/emscripten/emscripten-version.txt", "6.0.7\n");
        var cacheFlavor = RuntimePackTestData.Target(target).SystemLibraries.CacheFlavor;
        directory.Write(Path.Combine("cache", cacheFlavor, "libc.a"), string.Empty);
        return new(
            directory.PathTo("runtime-pack.json"),
            directory.PathTo("runtime-layout.json"),
            directory.Path,
            directory.Path,
            directory.PathTo("cache"),
            directory.PathTo("wasm-ld"),
            directory.PathTo("node"),
            directory.PathTo("run-wasm-tools.mjs"),
            directory.PathTo("wasm-tools.wasm"),
            directory.PathTo("output/runtime.wasm"),
            directory.PathTo("logs"),
            target,
            null,
            null);
    }

    private sealed class RecordingManifestReader(RuntimePackManifest result) : IRuntimePackManifestReader
    {
        public string? Path { get; private set; }

        public RuntimePackManifest Read(string path)
        {
            Path = path;
            return result;
        }
    }

    private sealed class RecordingLayoutReader(RuntimeLayout result) : IRuntimeLayoutReader
    {
        public string? Path { get; private set; }

        public RuntimeLayout Read(string path)
        {
            Path = path;
            return result;
        }
    }

    private sealed class RecordingLayoutCalculator(RuntimeMemoryLayout result) : IRuntimeMemoryLayoutCalculator
    {
        public RuntimeMemoryLayoutRequest? Request { get; private set; }

        public RuntimeMemoryLayout Calculate(RuntimeMemoryLayoutRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingAssetVerifier : IRuntimeAssetDigestVerifier
    {
        public List<(string Path, string Digest)> Verifications { get; } = [];

        public void Verify(string path, string expectedSha256) =>
            Verifications.Add((path, expectedSha256));
    }

    private sealed class RecordingArgumentBuilder(ImmutableArray<string> result) : IRuntimeLinkArgumentBuilder
    {
        public RuntimeLinkRequest? Request { get; private set; }

        public ImmutableArray<string> Build(RuntimeLinkRequest request)
        {
            Request = request;
            return result;
        }
    }

    private sealed class RecordingCommandInvoker : ICommandInvoker
    {
        public List<RuntimeCommand> Commands { get; } = [];

        public void Invoke(RuntimeCommand command) => Commands.Add(command);
    }

    private sealed class ConstantDigestCalculator(string result) : IArtifactDigestCalculator
    {
        public string Calculate(string path) => result;
    }
}
