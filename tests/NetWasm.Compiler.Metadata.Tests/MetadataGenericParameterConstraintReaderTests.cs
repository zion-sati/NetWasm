using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataGenericParameterConstraintReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsTypeAndMethodConstraintsAndCachesDeclarations(bool typeOwner)
    {
        var builder = new MetadataBuilder();
        builder.AddModule(0, builder.GetOrAddString("Library.dll"), builder.GetOrAddGuid(Guid.NewGuid()), default, default);
        var type = builder.AddTypeDefinition(TypeAttributes.Public, default, builder.GetOrAddString("Box`1"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var method = builder.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.IL,
            builder.GetOrAddString("Read"), default, 0, MetadataTokens.ParameterHandle(1));
        EntityHandle owner = typeOwner ? type : method;
        var parameter = builder.AddGenericParameter(owner, GenericParameterAttributes.ReferenceTypeConstraint,
            builder.GetOrAddString("T"), 0);
        var reference = builder.AddTypeReference(default, builder.GetOrAddString("Fixture"), builder.GetOrAddString("Required"));
        builder.AddGenericParameterConstraint(parameter, reference);
        var blob = new BlobBuilder();
        new MetadataRootBuilder(builder).Serialize(blob, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
        var identity = new AssemblyIdentity("Library");
        var metadata = new MetadataAssemblySnapshot(identity, provider.GetMetadataReader(),
            ImmutableDictionary<int, TypeDefinitionModel>.Empty, ImmutableDictionary<int, FieldDefinitionModel>.Empty,
            ImmutableDictionary<int, MethodDefinitionModel>.Empty, ImmutableDictionary<int, EntityHandle>.Empty);
        var required = new TypeDefinitionModel(new(identity, 0x02000002), "Fixture", "Required", false, [], []);
        var definitions = new Definitions(required);
        var reader = Assert.IsAssignableFrom<IGenericParameterConstraintReader>(new MetadataGenericParameterConstraintReader(
            ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty.Add(identity, metadata), definitions));
        var key = new EntityKey(identity, MetadataTokens.GetToken(owner));
        var first = reader.Read(key);
        var second = reader.Read(key);
        Assert.Equal(first, second);
        var result = Assert.Single(first);
        Assert.Equal(GenericParameterAttributes.ReferenceTypeConstraint, result.Attributes);
        Assert.Equal(CliTypeIdentity.FromDefinition(required), Assert.Single(result.Types));
        Assert.Equal(1, definitions.Calls);
        Assert.Throws<ArgumentException>(() => reader.Read(new(identity, 0x04000001)));
        Assert.Equal(DiagnosticCode.AssemblyResolution,
            Assert.Throws<CompilerException>(() => reader.Read(new(new("Missing"), key.MetadataToken))).Diagnostic.Code);
    }

    [Fact]
    public void RejectsNonContiguousParameterIndices()
    {
        var builder = new MetadataBuilder();
        builder.AddModule(0, builder.GetOrAddString("Library.dll"), builder.GetOrAddGuid(Guid.NewGuid()), default, default);
        var method = builder.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.IL,
            builder.GetOrAddString("Read"), default, 0, MetadataTokens.ParameterHandle(1));
        builder.AddGenericParameter(method, 0, builder.GetOrAddString("T"), 1);
        var blob = new BlobBuilder();
        new MetadataRootBuilder(builder).Serialize(blob, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(blob.ToImmutableArray());
        var identity = new AssemblyIdentity("Library");
        var metadata = new MetadataAssemblySnapshot(identity, provider.GetMetadataReader(),
            ImmutableDictionary<int, TypeDefinitionModel>.Empty, ImmutableDictionary<int, FieldDefinitionModel>.Empty,
            ImmutableDictionary<int, MethodDefinitionModel>.Empty, ImmutableDictionary<int, EntityHandle>.Empty);
        var reader = Assert.IsAssignableFrom<IGenericParameterConstraintReader>(new MetadataGenericParameterConstraintReader(
            ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty.Add(identity, metadata), new Definitions(null!)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata,
            Assert.Throws<CompilerException>(() => reader.Read(new(identity, MetadataTokens.GetToken(method)))).Diagnostic.Code);
    }

    private sealed class Definitions(TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public int Calls { get; private set; }
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) { Calls++; return definition; }
    }
}
