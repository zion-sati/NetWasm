using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeResolverTests
{
    [Fact]
    public void ForwardedDefinitionResolvesToThePlatformWithoutReadingOriginalMetadata()
    {
        var original = MetadataActorTestData.Type;
        var platform = new AssemblyIdentity("Platform");
        var target = original with { Key = original.Key with { Assembly = platform } };
        var source = new MetadataAssemblySnapshot(original.Key.Assembly, null!,
            new Dictionary<int, TypeDefinitionModel> { [0x02000001] = original },
            new Dictionary<int, FieldDefinitionModel>(), new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>())
        {
            AssemblyIdentityAliases = new AssemblyIdentityAliases(
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
                System.Collections.Immutable.ImmutableDictionary<(AssemblyIdentity, string), AssemblyIdentity>.Empty
                    .Add((original.Key.Assembly, original.FullName), platform))
        };
        var definitions = new RecordingTypeDefinitionResolver(target);
        var resolver = new MetadataTypeResolver(definitions);

        var result = ((IMetadataTypeResolver)resolver).Resolve(source, MetadataTokens.TypeDefinitionHandle(1));

        Assert.Same(target, result);
        Assert.Equal(platform, definitions.Identity!.Assembly);
        Assert.Equal(original.FullName, definitions.Identity.FullName);
    }

    [Fact]
    public void ResolveReadsDefinitionAndRejectsUnsupportedHandleShape()
    {
        var type = MetadataActorTestData.Type;
        const int typeToken = 0x02000001;
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            null!,
            new Dictionary<int, TypeDefinitionModel>
            {
                [typeToken] = type,
            },
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, System.Reflection.Metadata.EntityHandle>());
        var resolver = new MetadataTypeResolver(new RejectingTypeDefinitionResolver());
        var resolve = ((IMetadataTypeResolver)resolver).Resolve;

        Assert.Same(
            type,
            resolve(
                source,
                MetadataTokens.EntityHandle(typeToken)));
        Assert.Throws<CompilerException>(() => resolve(
            source,
            MetadataTokens.EntityHandle(0x04000001)));
    }

    [Fact]
    public void ResolveDecodesTypeSpecificationBeforeResolvingItsDefinition()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var expected = MetadataActorTestData.Type;
        var definitions = new RecordingTypeDefinitionResolver(expected);
        var resolver = new MetadataTypeResolver(definitions);

        var actual = ((IMetadataTypeResolver)resolver).Resolve(
            assembly.Metadata,
            MetadataTokens.TypeSpecificationHandle(1));

        Assert.Same(expected, actual);
        Assert.NotNull(definitions.Identity);
        Assert.Equal(CliTypeShape.GenericInstantiation, definitions.Identity.Shape);
    }

    [Fact]
    public void ResolveDecodesTypeReferenceBeforeResolvingItsDefinition()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.Library);
        var expected = MetadataActorTestData.Type;
        var definitions = new RecordingTypeDefinitionResolver(expected);
        var resolver = new MetadataTypeResolver(definitions);
        TypeReferenceHandle reference = assembly.Metadata.Reader.TypeReferences.First();

        var actual = ((IMetadataTypeResolver)resolver).Resolve(
            assembly.Metadata,
            reference);

        Assert.Same(expected, actual);
        Assert.NotNull(definitions.Identity);
    }

    private sealed class RejectingTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new InvalidOperationException("Type references are outside this test.");
    }

    private sealed class RecordingTypeDefinitionResolver(TypeDefinitionModel result) :
        ITypeDefinitionResolver
    {
        public CliTypeIdentity? Identity { get; private set; }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Identity = identity;
            return result;
        }
    }
}
