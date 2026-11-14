using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCompilationLoaderDependencyTests
{
    [Fact]
    public void MetadataViewMayShareAnIdentityWithoutReplacingRuntimeDefinitions()
    {
        var images = new Images(new()
        {
            ["entry"] = Image("Entry"),
            ["runtime"] = TypeImage("Library", "RuntimeType"),
            ["view"] = TypeImage("Library", "ReferenceOnlyType")
        });
        var loader = new MetadataCompilationLoader(
            new ManagedAssemblyLoader(images, new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(), new NativeCallbackDeclarationReader()),
            new MetadataCompilationFactory(), new TypeForwarderReader(images));

        using var lease = loader.Load("entry", ["runtime"], metadataReferencePaths: ["view"]);

        Assert.Equal(2, lease.Snapshot.Assemblies.Length);
        Assert.Contains(lease.Snapshot.Types, type => type.Name == "RuntimeType");
        Assert.DoesNotContain(lease.Snapshot.Types, type => type.Name == "ReferenceOnlyType");
    }

    [Fact]
    public void MetadataViewDoesNotSupplyMissingRuntimeDefinitions()
    {
        var images = new Images(new()
        {
            ["entry"] = Image("Entry", "Library"),
            ["view"] = TypeImage("Library", "ReferenceOnlyType")
        });
        var loader = new MetadataCompilationLoader(
            new ManagedAssemblyLoader(images, new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(), new NativeCallbackDeclarationReader()),
            new MetadataCompilationFactory(), new TypeForwarderReader(images));

        using var lease = loader.Load("entry", [], metadataReferencePaths: ["view"]);

        Assert.Single(lease.Snapshot.Assemblies);
        Assert.DoesNotContain(lease.Snapshot.Types, type => type.Name == "ReferenceOnlyType");
    }

    [Fact]
    public void LoadsMetadataWithAnUnresolvedUnusedAssemblyReference()
    {
        var images = new Images(new() { ["entry"] = Image("Entry", "Missing") });
        var loader = Assert.IsAssignableFrom<IMetadataCompilationLoader>(new MetadataCompilationLoader(
            new ManagedAssemblyLoader(images, new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(), new NativeCallbackDeclarationReader()),
            new MetadataCompilationFactory(), new TypeForwarderReader(images)));

        using var lease = loader.Load("entry", []);

        Assert.Equal("Entry", lease.Snapshot.EntryAssemblyIdentity.Name);
        Assert.Contains("Missing", Assert.Single(lease.Snapshot.Assemblies).References);
    }

    [Fact]
    public void StillRejectsDuplicateAssemblyInputs()
    {
        var images = new Images(new() { ["first"] = Image("Duplicate"), ["second"] = Image("Duplicate") });
        var loader = Assert.IsAssignableFrom<IMetadataCompilationLoader>(new MetadataCompilationLoader(
            new ManagedAssemblyLoader(images, new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(), new NativeCallbackDeclarationReader()),
            new MetadataCompilationFactory(), new TypeForwarderReader(images)));

        var error = Assert.Throws<CompilerException>(() => loader.Load("first", ["second"]));

        Assert.Equal(DiagnosticCode.DuplicateAssembly, error.Diagnostic.Code);
    }

    // Inputs load in parallel; a failure comes back as itself, the first in path order.
    [Fact]
    public void AnInputThatFailsToLoadFailsTheLoadWithItsOwnException()
    {
        var images = new Images(new() { ["entry"] = Image("Entry"), ["broken"] = [1, 2, 3] });
        // The forwarders are read first, from images that parse; the broken one fails as an assembly loads.
        var forwarderImages = new Images(new() { ["entry"] = Image("Entry"), ["broken"] = Image("Broken") });
        var loader = new MetadataCompilationLoader(
            new ManagedAssemblyLoader(images, new ValueTypeDefinitionStackKindResolver(),
                new NativeImportDeclarationReader(), new NativeCallbackDeclarationReader()),
            new MetadataCompilationFactory(), new TypeForwarderReader(forwarderImages));

        var error = Record.Exception(() => loader.Load("entry", ["broken"]));

        Assert.NotNull(error);
        Assert.IsNotType<AggregateException>(error);
    }

    private sealed class Images(Dictionary<string, byte[]> images) : IManagedAssemblyImageReader
    {
        public byte[] Read(string path) => images[path];
    }

    private static byte[] Image(string name, params string[] references)
        => TypeImage(name, null, references);

    private static byte[] TypeImage(string name, string? publicType, params string[] references)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new(1, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        if (publicType is not null)
            metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Fixture"),
                metadata.GetOrAddString(publicType), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        foreach (var reference in references)
            metadata.AddAssemblyReference(metadata.GetOrAddString(reference), new(1, 0), default, default, 0, default);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
    }
}
