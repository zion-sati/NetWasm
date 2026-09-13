using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentBuilderTests
{
    [Fact]
    public void BuildsManifestAndPackagesSelectedWorldThroughTheCapability()
    {
        var world = new WitWorld(0, "command", "wasi:cli@0.2.11", [], []);
        var document = new WitDocument([], [], [world], [], "{}");
        var documents = new RecordingDocumentReader(document);
        var worlds = new RecordingWorldValidator();
        var manifests = new RecordingManifestBuilder();
        var components = new RecordingPackager();
        var builder = AsBuilder(new ComponentBuilder(
            documents,
            worlds,
            manifests,
            new WitWorldSpecifierFormatter(),
            components));
        var request = new ComponentBuildRequest(
            "application.wasm",
            "runtime.wasm",
            "command.wit",
            "command",
            "application.component.wasm",
            ComponentTarget.Wasm32Wasi02,
            new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None));

        var result = builder.Build(request);

        Assert.Same(manifests.Manifest, result);
        Assert.Equal("command.wit", documents.Path);
        Assert.Same(document, worlds.Document);
        Assert.Same(world, worlds.World);
        Assert.Same(document, manifests.Document);
        Assert.Same(world, manifests.World);
        Assert.Equal(request.Target, manifests.Target);
        Assert.Equal(request.ManifestInputs, manifests.Inputs);
        Assert.Equal(request.CoreModulePath, components.Request!.CoreModulePath);
        Assert.Equal(request.RuntimeModulePath, components.Request.RuntimeModulePath);
        Assert.Equal("wasi:cli/command@0.2.11", components.Request.World);
        Assert.Equal(request.OutputPath, components.Request.OutputPath);
    }

    [Fact]
    public void RejectsNullBuildRequestThroughTheInterface()
    {
        var builder = AsBuilder(new ComponentBuilder(
            new RecordingDocumentReader(new([], [], [], [], "{}")),
            new RecordingWorldValidator(),
            new RecordingManifestBuilder(),
            new WitWorldSpecifierFormatter(),
            new RecordingPackager()));

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
    }

    [Fact]
    public void ConstructorRejectsEveryMissingCapability()
    {
        var document = new WitDocument([], [], [], [], "{}");
        var documents = new RecordingDocumentReader(document);
        var worlds = new RecordingWorldValidator();
        var manifests = new RecordingManifestBuilder();
        var specifiers = new WitWorldSpecifierFormatter();
        var components = new RecordingPackager();

        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuilder(null!, worlds, manifests, specifiers, components));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuilder(documents, null!, manifests, specifiers, components));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuilder(documents, worlds, null!, specifiers, components));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuilder(documents, worlds, manifests, null!, components));
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentBuilder(documents, worlds, manifests, specifiers, null!));
    }

    private sealed class RecordingDocumentReader(WitDocument document) : IWitDocumentReader
    {
        public string? Path { get; private set; }

        public WitDocument Read(string witPath)
        {
            Path = witPath;
            return document;
        }
    }

    private static IComponentBuilder AsBuilder(object builder) =>
        (IComponentBuilder)builder;

    private sealed class RecordingWorldValidator : IWitWorldValidator
    {
        public WitDocument? Document { get; private set; }
        public WitWorld? World { get; private set; }

        public void Validate(WitDocument document, WitWorld world)
        {
            Document = document;
            World = world;
        }
    }

    private sealed class RecordingManifestBuilder : IComponentManifestBuilder
    {
        public ComponentManifest Manifest { get; } = new(
            1,
            "wasi:cli@0.2.11",
            "command",
            "wasm32",
            "0.2",
            "utf8",
            "digest",
            "wasm-tools",
            [],
            [],
            [],
            []);

        public WitDocument? Document { get; private set; }
        public WitWorld? World { get; private set; }
        public ComponentTarget? Target { get; private set; }
        public ComponentManifestInputs? Inputs { get; private set; }

        public ComponentManifest Build(
            WitDocument document,
            WitWorld world,
            ComponentTarget target,
            ComponentManifestInputs inputs)
        {
            Document = document;
            World = world;
            Target = target;
            Inputs = inputs;
            return Manifest;
        }
    }

    private sealed class RecordingPackager : IComponentPackager
    {
        public ComponentPackageRequest? Request { get; private set; }

        public void Package(ComponentPackageRequest request) => Request = request;
    }
}
