using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataImplementedInterfaceResolverTests
{
    [Fact]
    public void GetInterfacesResolvesEveryHandleWithClosedTypeArguments()
    {
        var definition = new TypeDefinitionModel(
            new(new("Test"), 0x02000001),
            "Test",
            "Container`1",
            false,
            [],
            [])
        {
            GenericArity = 1,
        };
        var firstHandle = MetadataTokens.EntityHandle(0x01000001);
        var secondHandle = MetadataTokens.EntityHandle(0x01000002);
        var first = CliTypeIdentity.Named(new("Test"), "Test", "IFirst", false);
        var second = CliTypeIdentity.Named(new("Test"), "Test", "ISecond", false);
        var snapshot = Snapshot(definition, [firstHandle, secondHandle]);
        var signatures = new RecordingSignatureTypeResolver(
            new Dictionary<EntityHandle, CliTypeIdentity>
            {
                [firstHandle] = first,
                [secondHandle] = second,
            });
        var resolver = Create(definition, snapshot, signatures);
        var closed = CliTypeIdentity.GenericInstantiation(
            Identity(definition),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);

        var result = ((IImplementedInterfaceResolver)resolver).GetInterfaces(closed);
        var cachedInterfaces = ((IImplementedInterfaceResolver)resolver).GetInterfaces(closed);

        Assert.True(result == cachedInterfaces);

        Assert.Collection(
            result,
            item => Assert.Same(first, item),
            item => Assert.Same(second, item));
        Assert.All(signatures.Contexts, context =>
            Assert.Equal(closed.TypeArguments, context.TypeArguments));
    }

    [Fact]
    public void GetInterfacesReturnsEmptyForTypeWithoutDeclarations()
    {
        var definition = new TypeDefinitionModel(
            new(new("Test"), 0x02000001),
            "Test",
            "Plain",
            false,
            [],
            []);
        var resolver = Create(
            definition,
            Snapshot(definition, []),
            new RecordingSignatureTypeResolver(
                new Dictionary<EntityHandle, CliTypeIdentity>()));

        Assert.Empty(
            ((IImplementedInterfaceResolver)resolver).GetInterfaces(
                Identity(definition)));
    }

    private static MetadataImplementedInterfaceResolver Create(
        TypeDefinitionModel definition,
        MetadataAssemblySnapshot snapshot,
        IMetadataSignatureTypeResolver signatures)
    {
        var assemblies = new MetadataAssemblyResolver(
            ImmutableDictionary<string, MetadataAssemblySnapshot>.Empty.Add(
                snapshot.Identity.Name,
                snapshot),
            ImmutableDictionary<string, string>.Empty,
            MetadataActorTestData.CreateAvailabilityValidator());
        return new(
            new StubTypeDefinitionResolver(definition),
            assemblies,
            signatures);
    }

    private static MetadataAssemblySnapshot Snapshot(
        TypeDefinitionModel definition,
        ImmutableArray<EntityHandle> interfaces) => new(
            definition.Key.Assembly,
            null!,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>
            {
                [definition.Key.MetadataToken] = interfaces,
            });

    private static CliTypeIdentity Identity(TypeDefinitionModel definition) =>
        CliTypeIdentity.Named(
            definition.Key.Assembly,
            definition.Namespace,
            definition.Name,
            definition.IsValueType);

    private sealed class StubTypeDefinitionResolver(TypeDefinitionModel result) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => result;
    }

    private sealed class RecordingSignatureTypeResolver(
        IReadOnlyDictionary<EntityHandle, CliTypeIdentity> results) :
        IMetadataSignatureTypeResolver
    {
        internal List<CliGenericContext> Contexts { get; } = [];

        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle,
            CliGenericContext? genericContext = null)
        {
            Contexts.Add(genericContext!.Value);
            return results[handle];
        }
    }
}
