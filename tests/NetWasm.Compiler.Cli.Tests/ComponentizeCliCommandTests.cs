using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class ComponentizeCliCommandTests
{
    [Theory]
    [InlineData("None", FinalWasmOptimization.None, null, ManagedInliningHints.Auto)]
    [InlineData("O3", FinalWasmOptimization.O3, "IgnoreAggressive", ManagedInliningHints.IgnoreAggressive)]
    [InlineData("Oz", FinalWasmOptimization.Oz, "Preserve", ManagedInliningHints.Preserve)]
    public void PackagesTheComponentAndWritesItsManifest(
        string optimize, FinalWasmOptimization expectedOptimization, string? hints, ManagedInliningHints expectedHints)
    {
        var harness = new Harness("wasm32");
        string[] arguments =
        [
            "-coremodule:app.wasm", "-runtimemodule:runtime.wasm", "-wit:contract.wit", "-out:component.wasm",
            "-manifest:component.json", $"-optimize:{optimize}",
        ];

        var result = harness.Run(hints is null ? arguments : [.. arguments, $"-managedinlininghints:{hints}"]);

        Assert.Equal(0, result);
        Assert.True(harness.Validator.Called);
        var request = harness.Packager.Request!;
        Assert.Equal("app.wasm", request.CoreModulePath);
        Assert.Equal("runtime.wasm", request.RuntimeModulePath);
        Assert.Equal("component.wasm", request.OutputPath);
        Assert.Equal(ComponentTarget.Wasm32Wasi02, request.Target);
        Assert.Equal(expectedOptimization, request.Optimization);
        Assert.Equal(expectedHints, request.OptimizationOverrides?.ManagedInliningHints ?? ManagedInliningHints.Auto);
        var manifest = Assert.Single(harness.Files.TextWrites);
        Assert.Equal("component.json", manifest.Path);
        Assert.Contains("\"world\": \"main\"", manifest.Content, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', manifest.Content);
    }

    [Fact]
    public void PackagesAWasm64Component()
    {
        var harness = new Harness("wasm64");

        harness.Run(
            "-coremodule:app.wasm", "-wit:contract.wit", "-out:component.wasm", "-manifest:component.json",
            "-platform:wasm64");

        Assert.Equal(ComponentTarget.Wasm64Wasi02, harness.Packager.Request!.Target);
        Assert.Contains("wasm64", Assert.Single(harness.Files.TextWrites).Content, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidHintsBeforeReadingTheWitOrPackaging()
    {
        var harness = new Harness("wasm32");

        Assert.Throws<CompilerException>(() => harness.Run(
            "-coremodule:app.wasm", "-wit:contract.wit", "-out:component.wasm", "-manifest:component.json",
            "-managedinlininghints:invalid"));

        Assert.False(harness.Validator.Called);
        Assert.Null(harness.Packager.Request);
        Assert.Empty(harness.Files.TextWrites);
    }

    [Fact]
    public void ReportsTheCommandNameAndOptions()
    {
        var command = new Harness("wasm32").Command;

        Assert.Equal("componentize", command.Name);
        Assert.Same(ComponentizeCliOptions.Names, command.Options);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var documents = new RecordingDocuments();
        var validator = new RecordingValidator();
        var manifests = new RecordingManifestBuilder(null!);
        var packager = new RecordingPackager();
        var inputs = new RecordingManifestInputs();
        var files = new RecordingFiles();
        var hints = new ManagedInliningHintsParser();

        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(null!, validator, manifests, packager, inputs, files, hints));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(documents, null!, manifests, packager, inputs, files, hints));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(documents, validator, null!, packager, inputs, files, hints));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(documents, validator, manifests, null!, inputs, files, hints));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(documents, validator, manifests, packager, null!, files, hints));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(documents, validator, manifests, packager, inputs, null!, hints));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentizeCliCommand(documents, validator, manifests, packager, inputs, files, null!));
    }

    private sealed class Harness
    {
        public Harness(string target)
        {
            Command = new ComponentizeCliCommand(
                new RecordingDocuments(),
                Validator,
                new RecordingManifestBuilder(new ComponentManifest(
                    1, "example:test@1.0.0", "main", target, "0.2", "utf8", "hash", "wasm-tools 1", [], [], [], [])),
                Packager,
                new RecordingManifestInputs(),
                Files,
                new ManagedInliningHintsParser());
        }

        public RecordingValidator Validator { get; } = new();
        public RecordingPackager Packager { get; } = new();
        public RecordingFiles Files { get; } = new();
        public ComponentizeCliCommand Command { get; }

        public int Run(params string[] arguments) =>
            Command.Run(NwcTestArguments.Read(ComponentizeCliOptions.Names, arguments));
    }

    private sealed class RecordingDocuments : IWitDocumentReader
    {
        public WitDocument Read(string path) =>
            new([], [], [new WitWorld(0, "main", "example:test@1.0.0", [], [])], [], "{}");
    }

    private sealed class RecordingValidator : IWitWorldValidator
    {
        public bool Called { get; private set; }

        public void Validate(WitDocument document, WitWorld world) => Called = true;
    }

    private sealed class RecordingManifestBuilder(ComponentManifest manifest) : IComponentManifestBuilder
    {
        public ComponentManifest Build(
            WitDocument document, WitWorld world, ComponentTarget target, ComponentManifestInputs inputs) => manifest;
    }

    private sealed class RecordingPackager : IComponentPackager
    {
        public ComponentPackageRequest? Request { get; private set; }

        public void Package(ComponentPackageRequest request) => Request = request;
    }

    private sealed class RecordingManifestInputs : IComponentManifestInputReader
    {
        public ComponentManifestInputs Read(ComponentizeCliOptions options) =>
            new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None);
    }
}
