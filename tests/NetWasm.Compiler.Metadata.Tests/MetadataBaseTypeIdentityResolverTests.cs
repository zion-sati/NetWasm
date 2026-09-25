using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataBaseTypeIdentityResolverTests
{
    [Fact]
    public void GetBaseTypeIdentityReturnsNullOrResolvedIdentityWithGenericContext()
    {
        var current = Type(0x02000001, "Current`1");
        var currentIdentity = CliTypeIdentity.GenericInstantiation(
            Identity(current),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var expected = CliTypeIdentity.Named(
            current.Key.Assembly,
            "Test",
            "Base`1",
            false);
        var signatureTypes = new RecordingSignatureTypeResolver(expected);
        var resolver = Create(
            current,
            MetadataTokens.EntityHandle(0x02000002),
            signatureTypes);

        Assert.Same(
            expected,
            ((IBaseTypeIdentityResolver)resolver).GetBaseTypeIdentity(currentIdentity));
        Assert.Equal(currentIdentity.TypeArguments, signatureTypes.Context!.Value.TypeArguments);

        var plainIdentity = Identity(current);
        Assert.Same(
            expected,
            ((IBaseTypeIdentityResolver)resolver).GetBaseTypeIdentity(plainIdentity));
        Assert.Empty(signatureTypes.Context!.Value.TypeArguments);

        resolver = Create(current, default, signatureTypes);
        Assert.Null(
            ((IBaseTypeIdentityResolver)resolver).GetBaseTypeIdentity(currentIdentity));
    }

    private static MetadataBaseTypeIdentityResolver Create(
        TypeDefinitionModel current,
        EntityHandle baseType,
        IMetadataSignatureTypeResolver signatureTypes)
    {
        var snapshot = new MetadataAssemblySnapshot(
            current.Key.Assembly,
            null!,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>
            {
                [current.Key.MetadataToken] = baseType,
            });
        var assemblies = new MetadataAssemblyResolver(
            ImmutableDictionary<string, MetadataAssemblySnapshot>.Empty.Add(
                current.Key.Assembly.Name,
                snapshot),
            ImmutableDictionary<string, string>.Empty,
            MetadataActorTestData.CreateAvailabilityValidator());
        return new(
            new StubTypeDefinitionResolver(current),
            assemblies,
            signatureTypes);
    }

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

    private sealed class StubTypeDefinitionResolver(TypeDefinitionModel result) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => result;
    }

    private sealed class RecordingSignatureTypeResolver(CliTypeIdentity result) :
        IMetadataSignatureTypeResolver
    {
        internal CliGenericContext? Context { get; private set; }

        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle,
            CliGenericContext? genericContext = null)
        {
            Context = genericContext;
            return result;
        }
    }
}
