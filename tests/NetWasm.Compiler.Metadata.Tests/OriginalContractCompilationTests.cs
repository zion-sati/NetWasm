using NetWasm.Compiler.Core.NativeInterop;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class OriginalContractCompilationTests
{
    [Fact]
    public void OriginalImagesAreIsolatedEvenWhenTheyShareRuntimeIdentity()
    {
        using var assets = TestAssets.Create();
        var images = new RecordingLoader();
        var loader = Assert.IsAssignableFrom<IMetadataCompilationLoader>(CreateLoader(images));
        var lease = loader.Load(assets.Application, [assets.Library, assets.CoreLib],
            originalContractReferencePaths: [assets.Library]);
        var snapshot = lease.Snapshot;
        var contract = Assert.Single(snapshot.OriginalContractAssemblies);
        var runtime = snapshot.Assemblies.Single(a => a.Identity == contract.Identity);
        Assert.NotSame(runtime, contract);
        Assert.Equal(3, snapshot.Assemblies.Length);
        Assert.Equal(snapshot.Assemblies.Sum(a => a.Types.Count), snapshot.Types.Length);
        Assert.Equal(snapshot.Assemblies.Sum(a => a.Methods.Count), snapshot.Methods.Length);
        Assert.DoesNotContain(snapshot.Types, type => contract.Types.Values.Any(original => ReferenceEquals(type, original)));
        Assert.DoesNotContain(snapshot.Methods, method => contract.Methods.Values.Any(original => ReferenceEquals(method, original)));
        var materialized = new MetadataCompilationMaterializationFactory().Create(snapshot);
        Assert.Equal(contract.Types.Count, materialized.OriginalContractTypes.Count);
        Assert.All(materialized.OriginalContractTypes.Values, item => Assert.Same(contract.Metadata, item.Source));
        Assert.Same(runtime.Metadata, materialized.MetadataAssemblies[contract.Identity.Name]);
        Assert.All(materialized.OriginalContractTypes.Values, item =>
            Assert.Same(item.Definition, contract.Types[item.Definition.Key.MetadataToken]));

        lease.Dispose();
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Snapshot);
        Assert.All(images.Loaded, image => Assert.Throws<ObjectDisposedException>(
            () => _ = image.PortableExecutableReader));
    }

    [Fact]
    public void DuplicateOriginalIdentitiesRejectAndDisposeBothRoles()
    {
        using var assets = TestAssets.Create();
        var images = new RecordingLoader();
        var error = Assert.Throws<CompilerException>(() => CreateLoader(images).Load(
            assets.Application, [assets.CoreLib],
            originalContractReferencePaths: [assets.Library, assets.Library]));
        Assert.Equal(DiagnosticCode.DuplicateAssembly, error.Diagnostic.Code);
        Assert.Equal(4, images.Loaded.Count);
        Assert.All(images.Loaded, image => Assert.Throws<ObjectDisposedException>(
            () => _ = image.PortableExecutableReader));
    }

    [Fact]
    public void LaterOriginalLoadFailureDisposesEarlierImages()
    {
        using var assets = TestAssets.Create();
        var images = new RecordingLoader { FailingPath = "unavailable-contract" };
        Assert.Throws<InvalidOperationException>(() => CreateLoader(images).Load(
            assets.Application, [assets.CoreLib],
            originalContractReferencePaths: [assets.Library, "unavailable-contract"]));
        Assert.Equal(3, images.Loaded.Count);
        Assert.All(images.Loaded, image => Assert.Throws<ObjectDisposedException>(
            () => _ = image.PortableExecutableReader));
    }

    private static MetadataCompilationLoader CreateLoader(RecordingLoader images) =>
        new MetadataCompilationLoader(images, new MetadataCompilationFactory(),
            new TypeForwarderReader(new ManagedAssemblyImageReader()));

    private sealed class RecordingLoader : IManagedAssemblyLoader
    {
        public List<ManagedAssembly> Loaded { get; } = [];
        public string? FailingPath { get; init; }

        public ManagedAssembly Load(string path, ImmutableDictionary<string, string> aliases,
            ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity>? forwarders = null,
            INativeImportBinder? nativeBinding = null)
        {
            if (path == FailingPath) throw new InvalidOperationException();
            var image = new ManagedAssemblyLoader(new ManagedAssemblyImageReader(),
                new ValueTypeDefinitionStackKindResolver(), new NativeImportDeclarationReader(),
                new NativeCallbackDeclarationReader()).Load(path, aliases, forwarders, nativeBinding);
            Loaded.Add(image);
            return image;
        }
    }
}
