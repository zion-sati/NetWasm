using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataBaseTypeResolverTests
{
    [Fact]
    public void GetBaseTypeResolvesEntityAndNormalizesIdentity()
    {
        var current = Type(0x02000001, "Current");
        var baseType = Type(0x02000002, "Base");
        var snapshot = Snapshot(
            current,
            MetadataTokens.EntityHandle(baseType.Key.MetadataToken));
        var assemblies = Resolver(snapshot);
        var resolver = new MetadataBaseTypeResolver(
            assemblies,
            new StubMetadataTypeResolver(baseType));

        Assert.Equal(
            baseType.Key,
            ((IMetadataEntityBaseTypeResolver)resolver).GetBaseType(current.Key));

        var identityResolver = new MetadataIdentityBaseTypeResolver(
            new StubBaseTypeIdentityResolver(CliTypeIdentity.Named(
                new("Core"),
                "System",
                "Int32",
                true)));
        Assert.Equal(
            "primitive:i4",
            ((IMetadataIdentityBaseTypeResolver)identityResolver).GetBaseType(Identity(current))!
                .CanonicalName);
    }

    [Fact]
    public void GetBaseTypeReturnsNullForRootEntityAndIdentity()
    {
        var current = Type(0x02000001, "Current");
        var resolver = new MetadataBaseTypeResolver(
            Resolver(Snapshot(current, default)),
            new StubMetadataTypeResolver(current));

        Assert.Null(((IMetadataEntityBaseTypeResolver)resolver).GetBaseType(current.Key));
        var identityResolver = new MetadataIdentityBaseTypeResolver(
            new StubBaseTypeIdentityResolver(null));
        Assert.Null(((IMetadataIdentityBaseTypeResolver)identityResolver).GetBaseType(Identity(current)));
    }

    private static MetadataAssemblySnapshot Snapshot(
        TypeDefinitionModel type,
        EntityHandle baseType) => new(
            type.Key.Assembly,
            null!,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>
            {
                [type.Key.MetadataToken] = baseType,
            });

    private static MetadataAssemblyResolver Resolver(MetadataAssemblySnapshot snapshot) =>
        new(
            ImmutableDictionary<string, MetadataAssemblySnapshot>.Empty.Add(
                snapshot.Identity.Name,
                snapshot),
            ImmutableDictionary<string, string>.Empty,
            MetadataActorTestData.CreateAvailabilityValidator());

    private static TypeDefinitionModel Type(int token, string name) => new(
        new(new("Test.Assembly"), token),
        "Test",
        name,
        false,
        [],
        []);

    private static CliTypeIdentity Identity(TypeDefinitionModel type) =>
        CliTypeIdentity.Named(
            type.Key.Assembly,
            type.Namespace,
            type.Name,
            type.IsValueType);

    private sealed class StubMetadataTypeResolver(TypeDefinitionModel result) :
        IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle) => result;
    }

    private sealed class StubBaseTypeIdentityResolver(CliTypeIdentity? result) :
        IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) => result;
    }
}
