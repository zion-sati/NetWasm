using System.Collections.Immutable;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.Tests.Artifacts;

public sealed class CompilerArtifactManifestTests
{
    [Fact]
    public void DigestCalculatorReturnsTheCanonicalSha256Digest()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "input.dll");
        File.WriteAllText(path, "abc");

        var digest = new Sha256ArtifactFileDigestCalculator().Calculate(path);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", digest);
    }

    [Fact]
    public void BuilderSortsIdentitiesAndAddsSemanticMetadata()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("app.dll", "input");
        var reference = directory.Write("ref.dll", "reference");
        var core = directory.Write("app.core.wasm", "wasm");
        var layout = directory.Write("runtime-layout.json", "layout");
        var request = CreateRequest(directory.Path, input, reference, core, layout);

        var result = new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator()).Build(request);

        Assert.Equal(1, result.Manifest.SchemaVersion);
        Assert.Equal("wasm64", result.Manifest.Target);
        Assert.Equal("netwasm0.1", result.Manifest.Profile);
        Assert.Equal("netwasm-sdk-preview", result.Manifest.SdkVersion);
        Assert.Equal("compiler-preview", result.Manifest.CompilerVersion);
        Assert.Equal("runtime-preview", result.Manifest.RuntimeVersion);
        Assert.Equal("abi-v1", result.Manifest.RuntimeAbiVersion);
        Assert.Equal(["InputAssembly", "Reference"], result.Manifest.Inputs.Select(static item => item.Kind));
        Assert.Equal(["CoreModule", "RuntimeLayout"], result.Manifest.Artifacts.Select(static item => item.Kind));
        Assert.Equal("app.dll", result.Manifest.Inputs[0].Path);
        Assert.Equal("app.core.wasm", result.Manifest.Artifacts[0].Path);
        Assert.Equal(64, result.Manifest.SemanticBuildId.Length);
        Assert.All(result.Manifest.Artifacts, artifact =>
        {
            Assert.Equal(1, artifact.SchemaVersion);
            Assert.Equal(result.Manifest.SemanticBuildId, artifact.SemanticBuildId);
            Assert.Equal(result.Manifest.Target, artifact.Target);
            Assert.Equal(result.Manifest.Profile, artifact.Profile);
            Assert.Equal(64, artifact.Sha256.Length);
        });
        Assert.Equal(core, result.Artifacts[0].FullPath);
    }

    [Fact]
    public void BuilderIsDeterministicForTheSameInputs()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("app.dll", "input");
        var core = directory.Write("app.core.wasm", "wasm");
        var layout = directory.Write("runtime-layout.json", "layout");
        var request = CreateRequest(directory.Path, input, input, core, layout);
        var builder = new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator());

        var first = builder.Build(request).Manifest;
        var second = builder.Build(request).Manifest;

        Assert.Equal(first with { Inputs = [], Artifacts = [] }, second with { Inputs = [], Artifacts = [] });
        Assert.True(first.Inputs.SequenceEqual(second.Inputs));
        Assert.True(first.Artifacts.SequenceEqual(second.Artifacts));
    }

    [Fact]
    public void BuilderUsesAnExternalPathNamespaceOutsideTheProject()
    {
        using var directory = new TemporaryDirectory();
        var project = Directory.CreateDirectory(Path.Combine(directory.Path, "project")).FullName;
        var external = directory.Write("shared.dll", "shared");
        var core = Path.Combine(project, "app.core.wasm");
        File.WriteAllText(core, "wasm");
        var request = CreateRequest(project, external, external, core, core);

        var result = new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator()).Build(request);

        Assert.Equal("external/shared.dll", result.Manifest.Inputs[0].Path);
    }

    [Fact]
    public void WriterAndReaderRoundTripTheManifest()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("app.dll", "input");
        var core = directory.Write("app.core.wasm", "wasm");
        var layout = directory.Write("runtime-layout.json", "layout");
        var request = CreateRequest(directory.Path, input, input, core, layout);
        var manifest = new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator()).Build(request).Manifest;
        var path = Path.Combine(directory.Path, "nested", "manifest.json");

        new CompilerArtifactManifestWriter().Write(new(path, manifest));
        var read = new CompilerArtifactManifestReader().Read(path);

        Assert.Equal(manifest with { Inputs = [], Artifacts = [] }, read with { Inputs = [], Artifacts = [] });
        Assert.True(manifest.Inputs.SequenceEqual(read.Inputs));
        Assert.True(manifest.Artifacts.SequenceEqual(read.Artifacts));
        var json = File.ReadAllText(path);
        Assert.Contains("\"semanticBuildId\"", json, StringComparison.Ordinal);
        Assert.Contains('\n', json);
        Assert.DoesNotContain('\r', json);
    }

    [Fact]
    public void ValidatorRejectsChangedOutputs()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("app.dll", "input");
        var core = directory.Write("app.core.wasm", "wasm");
        var layout = directory.Write("runtime-layout.json", "layout");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var request = CreateRequest(directory.Path, input, input, core, layout) with { ManifestPath = manifestPath };
        var builder = new CompilerArtifactManifestBuilder(new Sha256ArtifactFileDigestCalculator());
        var manifest = builder.Build(request).Manifest;
        new CompilerArtifactManifestWriter().Write(new(manifestPath, manifest));
        File.AppendAllText(core, "changed");

        var validator = new CompilerArtifactManifestValidator(builder, new CompilerArtifactManifestReader());

        var exception = Assert.Throws<InvalidOperationException>(() => validator.Validate(new(request, manifestPath)));
        Assert.Equal("NetWasm artifact manifest is stale.", exception.Message);
    }

    [Fact]
    public void ValidatorReturnsTheCurrentResolvedArtifacts()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("app.dll", "input");
        var core = directory.Write("app.core.wasm", "wasm");
        var layout = directory.Write("runtime-layout.json", "layout");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        var request = CreateRequest(directory.Path, input, input, core, layout) with
        {
            ManifestPath = manifestPath,
        };
        var builder = new CompilerArtifactManifestBuilder(
            new Sha256ArtifactFileDigestCalculator());
        var expected = builder.Build(request);
        new CompilerArtifactManifestWriter().Write(new(manifestPath, expected.Manifest));
        var validator = Assert.IsAssignableFrom<ICompilerArtifactManifestValidator>(
            new CompilerArtifactManifestValidator(
                builder,
                new CompilerArtifactManifestReader()));

        var actual = validator.Validate(new(request, manifestPath));

        Assert.Equal(expected.Manifest.SemanticBuildId, actual.Manifest.SemanticBuildId);
        Assert.True(expected.Artifacts.SequenceEqual(actual.Artifacts));
    }

    [Fact]
    public void BuilderRejectsMissingOutput()
    {
        using var directory = new TemporaryDirectory();
        var input = directory.Write("app.dll", "input");
        var request = CreateRequest(
            directory.Path,
            input,
            input,
            Path.Combine(directory.Path, "missing.wasm"),
            Path.Combine(directory.Path, "layout.json"));

        Assert.Throws<FileNotFoundException>(() => new CompilerArtifactManifestBuilder(
            new Sha256ArtifactFileDigestCalculator()).Build(request));
    }

    private static CompilerArtifactManifestBuildRequest CreateRequest(
        string projectDirectory,
        string input,
        string reference,
        string core,
        string layout) => new(
            Path.Combine(projectDirectory, "manifest.json"),
            projectDirectory,
            "netwasm0.1",
            "wasm64",
            "simd-disabled",
            "netwasm-sdk-preview",
            "compiler-preview",
            "abi-v1",
            "runtime-preview",
            [
                new("Reference", reference),
                new("InputAssembly", input),
            ],
            [
                new("RuntimeLayout", "application/json", layout),
                new("CoreModule", "application/wasm", core),
            ]);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-manifest-{Guid.NewGuid():N}");
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
