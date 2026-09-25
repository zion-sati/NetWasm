using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeEntityResolverTests
{
    [Fact]
    public void ResolveReturnsTheResolvedTypeKey()
    {
        Func<IMetadataTypeEntityResolver, EntityKey> contract = Resolve;
        var key = contract(new MetadataTypeEntityResolver(new StubTypeResolver()));

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

    private sealed class StubTypeResolver : IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(MetadataAssemblySnapshot source, EntityHandle handle) =>
            MetadataActorTestData.Type;
    }
}
