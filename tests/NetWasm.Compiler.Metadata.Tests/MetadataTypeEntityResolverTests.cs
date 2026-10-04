using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeEntityResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveForwardsTheClosedCatchContextBeforeLookingUpItsDefinition(bool methodParameter)
    {
        var exception = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(MetadataActorTestData.Assembly, "Fixtures", "Exception`1", false),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var context = methodParameter
            ? new CliGenericContext([], [exception])
            : new CliGenericContext([exception], []);
        var signatures = new ContextSignatureResolver(context, exception);
        var definitions = new IdentityTypeResolver(exception);
        var reader = (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader));
        var source = new MetadataAssemblySnapshot(MetadataActorTestData.Assembly, reader,
            new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = Assert.IsAssignableFrom<IMetadataTypeEntityResolver>(
            new MetadataTypeEntityResolver(signatures, definitions));

        Assert.Equal(MetadataActorTestData.TypeKey, resolver.Resolve(source, default, context));
        Assert.Equal(1, signatures.Calls);
        Assert.Equal(1, definitions.Calls);
    }

    [Fact]
    public void ResolveReturnsTheResolvedTypeKey()
    {
        Func<IMetadataTypeEntityResolver, EntityKey> contract = Resolve;
        var key = contract(new MetadataTypeEntityResolver(new StubSignatureResolver(), new StubTypeResolver()));

        Assert.Equal(MetadataActorTestData.TypeKey, key);
    }

    private static EntityKey Resolve(IMetadataTypeEntityResolver resolver)
    {
        var reader = (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader));
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            reader,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());

        return resolver.Resolve(source, default);
    }

    private sealed class StubSignatureResolver : IMetadataSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(MetadataAssemblySnapshot source, EntityHandle handle, CliGenericContext? genericContext = null) =>
            CliTypeIdentity.FromDefinition(MetadataActorTestData.Type);
    }

    private sealed class StubTypeResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            MetadataActorTestData.Type;
    }

    private sealed class ContextSignatureResolver(CliGenericContext expected, CliTypeIdentity identity) : IMetadataSignatureTypeResolver
    {
        internal int Calls;
        public CliTypeIdentity Resolve(MetadataAssemblySnapshot source, EntityHandle handle, CliGenericContext? genericContext = null)
        {
            Assert.Equal(expected, genericContext);
            Calls++;
            return identity;
        }
    }

    private sealed class IdentityTypeResolver(CliTypeIdentity expected) : ITypeDefinitionResolver
    {
        internal int Calls;
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Assert.Equal(expected, identity);
            Calls++;
            return MetadataActorTestData.Type;
        }
    }
}
