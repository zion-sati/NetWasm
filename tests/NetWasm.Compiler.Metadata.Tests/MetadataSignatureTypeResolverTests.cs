using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataSignatureTypeResolverTests
{
    [Fact]
    public void ResolveMapsDefinitionThroughTypeAndIdentityResolvers()
    {
        var expected = CliTypeIdentity.Named(
            MetadataActorTestData.Assembly,
            "Test.Namespace",
            "Sample",
            false);
        var resolver = new MetadataSignatureTypeResolver(
            new StubTypeResolver(MetadataActorTestData.Type),
            new StubTypeIdentityResolver(expected));
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            null!,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, System.Reflection.Metadata.EntityHandle>());

        var actual = ((IMetadataSignatureTypeResolver)resolver).Resolve(
            source,
            MetadataTokens.EntityHandle(MetadataActorTestData.TypeKey.MetadataToken));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolveDecodesTypeSpecificationsThroughItsInterface()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var resolver = new MetadataSignatureTypeResolver(
            new StubTypeResolver(MetadataActorTestData.Type),
            new StubTypeIdentityResolver(CliTypeIdentity.Named(
                MetadataActorTestData.Assembly, "Test.Namespace", "Sample", false)));

        var actual = ((IMetadataSignatureTypeResolver)resolver).Resolve(
            assembly.Metadata,
            MetadataTokens.TypeSpecificationHandle(1),
            new CliGenericContext([], []));

        Assert.NotNull(actual);
    }

    [Fact]
    public void ResolveDecodesTypeSpecificationsWithAnEmptyGenericContext()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var resolver = new MetadataSignatureTypeResolver(
            new StubTypeResolver(MetadataActorTestData.Type),
            new StubTypeIdentityResolver(CliTypeIdentity.Named(
                MetadataActorTestData.Assembly, "Test.Namespace", "Sample", false)));

        var actual = ((IMetadataSignatureTypeResolver)resolver).Resolve(
            assembly.Metadata,
            MetadataTokens.TypeSpecificationHandle(1));

        Assert.NotNull(actual);
    }

    private sealed class StubTypeResolver(TypeDefinitionModel result) : IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(
            MetadataAssemblySnapshot source,
            System.Reflection.Metadata.EntityHandle handle) => result;
    }

    private sealed class StubTypeIdentityResolver(CliTypeIdentity result) :
        ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => result;
    }
}
