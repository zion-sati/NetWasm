using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Generator;

namespace NetWasm.Hosting.Generator.Tests;

public sealed class HostingPlatformCatalogProjectorTests
{
    [Fact]
    public void ProjectsCallableProvidersAndAuditsEmptyMarkerInterfaces()
    {
        var capabilities = new RecordingClassifier();
        var hashes = new RecordingHasher();
        var projector = new HostingPlatformCatalogProjector(capabilities, hashes);
        var function = new WitInterfaceFunction(
            "wasi:test/callable@1.0.0", "run", ["string"], ["u32"]);
        var catalog = new WitInterfaceCatalog(
            "wasi:test/command@1.0.0",
            "normalized",
            [
                new("wasi:test/callable@1.0.0", [function]),
                new("wasi:test/marker@1.0.0", []),
            ]);
        var shim = new Preview2ShimIdentity("shim", "1.0.0");

        var result = projector.Project(catalog, shim);

        Assert.Equal(1, result.Schema);
        Assert.Equal(catalog.World, result.World);
        Assert.Equal(hashes.Output, result.WitSha256);
        Assert.Same(shim, result.Shim);
        var provider = Assert.Single(result.Providers);
        Assert.Equal(function.Interface, provider.Module);
        Assert.Equal(NetWasmPlatformCapability.Baseline, provider.Capability);
        var projected = Assert.Single(provider.Functions);
        Assert.Equal(function.Name, projected.Name);
        Assert.Equal<string>(function.Parameters, projected.Parameters);
        Assert.Equal<string>(function.Results, projected.Results);
        Assert.Equal(catalog.Interfaces.Select(value => value.Module), capabilities.Modules);
        Assert.Equal(catalog.NormalizedWitJson, hashes.Input);
    }

    [Fact]
    public void RejectsMissingDependenciesInputsAndIncompleteCatalogs()
    {
        var capabilities = new RecordingClassifier();
        var hashes = new RecordingHasher();
        var valid = new WitInterfaceCatalog("world", "json", [new("module", [])]);
        var shim = new Preview2ShimIdentity("shim", "version");

        Assert.Throws<ArgumentNullException>(() =>
            new HostingPlatformCatalogProjector(null!, hashes));
        Assert.Throws<ArgumentNullException>(() =>
            new HostingPlatformCatalogProjector(capabilities, null!));
        var projector = new HostingPlatformCatalogProjector(capabilities, hashes);
        Assert.Throws<ArgumentNullException>(() => projector.Project(null!, shim));
        Assert.Throws<ArgumentNullException>(() => projector.Project(valid, null!));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with { Interfaces = default }, shim));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with { Interfaces = [] }, shim));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with { World = " " }, shim));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with { NormalizedWitJson = "" }, shim));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with { Interfaces = [null!] }, shim));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with
            {
                Interfaces = [new("module", default)]
            }, shim));
        Assert.Throws<InvalidDataException>(() =>
            projector.Project(valid with
            {
                Interfaces = [new("module", [null!])]
            }, shim));
    }

    [Fact]
    public void PropagatesCapabilityClassificationFailureBeforeHashing()
    {
        var failure = new InvalidOperationException();
        var capabilities = new RecordingClassifier { Failure = failure };
        var hashes = new RecordingHasher();
        var catalog = new WitInterfaceCatalog("world", "json", [new("module", [])]);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            new HostingPlatformCatalogProjector(capabilities, hashes).Project(
                catalog, new("shim", "version"))));
        Assert.Null(hashes.Input);
    }

    [Fact]
    public void HashesUtf8TextDeterministically()
    {
        var hasher = new Sha256TextHasher();

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            hasher.Hash("abc"));
        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
    }

    private sealed class RecordingClassifier : IWasiPreview2CapabilityClassifier
    {
        public List<string> Modules { get; } = [];
        public Exception? Failure { get; init; }

        public NetWasmPlatformCapability Classify(string module)
        {
            Modules.Add(module);
            if (Failure is not null) throw Failure;
            return NetWasmPlatformCapability.Baseline;
        }
    }

    private sealed class RecordingHasher : ITextHasher
    {
        public string Output { get; } = new('a', 64);
        public string? Input { get; private set; }

        public string Hash(string value)
        {
            Input = value;
            return Output;
        }
    }
}
