using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core.UnsafeAccessors;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class UnsafeAccessorDeclarationReaderTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "field")]
    public void PreservesKindAndNamedArgumentDistinctions(bool named, string? name)
    {
        using var fixture = Create(named: named, name: name);
        Assert.Equal(new UnsafeAccessorDeclaration(3, name, named, false), Read(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsAttributeConstructorsFromDefinitionsAndReferences(bool definition)
    {
        using var fixture = Create(constructorDefinition: definition);
        Assert.Equal(3, Read(fixture)!.Kind);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("unrelated")]
    [InlineData("type-specification")]
    [InlineData("physical-body")]
    public void IgnoresMethodsThatDoNotRequireAccessorSynthesis(string shape)
    {
        using var fixture = Create(shape: shape);
        Assert.Null(Read(fixture));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("no-kind")]
    [InlineData("wrong-kind")]
    [InlineData("unknown-name")]
    [InlineData("duplicate-name")]
    [InlineData("field-name")]
    [InlineData("wrong-name-type")]
    [InlineData("wrong-name-value")]
    public void RetainsMalformedFactsInsteadOfRejectingUnusedDeclarations(string shape)
    {
        using var fixture = Create(shape: shape);
        Assert.True(Read(fixture)!.IsMalformed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordsParameterAndReturnTypeTranslation(bool returnParameter)
    {
        using var fixture = Create(shape: "translation", returnParameter: returnParameter);
        var result = Read(fixture)!;
        Assert.True(result.HasTypeTranslation);
        Assert.False(result.IsMalformed);
        Assert.Equal(returnParameter ? TranslatedType : null, result.TypeTranslations!.Return);
        Assert.Equal(returnParameter ? null : TranslatedType, result.TypeTranslations.Parameter(0));
        Assert.Null(result.TypeTranslations.Parameter(1));
    }

    [Theory]
    [InlineData("translation-malformed")]
    [InlineData("translation-truncated")]
    public void ATranslationThatNamesNoTypeIsMalformed(string shape)
    {
        using var fixture = Create(shape: shape);
        var result = Read(fixture)!;
        Assert.True(result.IsMalformed);
        Assert.False(result.HasTypeTranslation);
    }

    [Fact]
    public void UnrelatedParameterAttributesDoNotRequestTypeTranslation()
    {
        using var fixture = Create(shape: "unrelated-parameter");
        var result = Read(fixture)!;
        Assert.False(result.HasTypeTranslation);
        Assert.False(result.IsMalformed);
    }

    private const string TranslatedType = "Fixture.Target, Fixture";

    private static UnsafeAccessorDeclaration? Read(Fixture fixture)
    {
        var reader = Assert.IsAssignableFrom<IUnsafeAccessorDeclarationReader>(
            new UnsafeAccessorDeclarationReader(new CustomAttributeTypeNameProvider()));
        return reader.Read(fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));
    }

    private static Fixture Create(bool named = false, string? name = null,
        string shape = "normal", bool constructorDefinition = false, bool returnParameter = false)
    {
        var metadata = new MetadataBuilder();
        var module = metadata.AddModule(0, metadata.GetOrAddString("Accessors"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public, default, metadata.GetOrAddString("Declarations"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0, result => result.Void(), _ => { });
        var parameter = metadata.AddParameter(ParameterAttributes.None, default, returnParameter ? 0 : 1);
        var method = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL, metadata.GetOrAddString("Accessor"), metadata.GetOrAddBlob(signature),
            shape == "physical-body" ? 1 : 0, parameter);
        EntityHandle type = constructorDefinition
            ? metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString("UnsafeAccessorAttribute"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(2))
            : metadata.AddTypeReference(module, metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString(shape == "unrelated" ? "OtherAttribute" : "UnsafeAccessorAttribute"));
        if (shape == "type-specification")
        {
            var specification = new BlobBuilder();
            new BlobEncoder(specification).TypeSpecificationSignature().GenericInstantiation(type, 1, false)
                .AddArgument().Int32();
            type = metadata.AddTypeSpecification(metadata.GetOrAddBlob(specification));
        }
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(isInstanceMethod: true).Parameters(
            shape == "no-kind" ? 0 : 1, result => result.Void(), parameters =>
            {
                if (shape == "no-kind") return;
                if (shape == "wrong-kind") parameters.AddParameter().Type().String();
                else parameters.AddParameter().Type().Int32();
            });
        EntityHandle constructor = constructorDefinition
            ? metadata.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.IL,
                metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(constructorSignature), 0,
                MetadataTokens.ParameterHandle(2))
            : metadata.AddMemberReference(type, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(constructorSignature));
        var payload = new BlobBuilder();
        payload.WriteUInt16(1);
        if (shape != "truncated")
        {
            if (shape == "wrong-kind") payload.WriteSerializedString("3");
            else if (shape != "no-kind") payload.WriteInt32(3);
            int namedCount = shape == "duplicate-name" ? 2 :
                named || shape is "unknown-name" or "field-name" or "wrong-name-type" or "wrong-name-value" ? 1 : 0;
            payload.WriteUInt16((ushort)namedCount);
            for (int index = 0; index < namedCount; index++)
            {
                payload.WriteByte(shape == "field-name" ? (byte)0x53 : (byte)0x54);
                payload.WriteByte(shape == "wrong-name-value" ? (byte)0x55 : shape == "wrong-name-type" ? (byte)0x08 : (byte)0x0e);
                // An enum whose serialized type name is String still carries an
                // integer payload. It must not be mistaken for the Name property.
                if (shape == "wrong-name-value") payload.WriteSerializedString("String");
                payload.WriteSerializedString(shape == "unknown-name" ? "Other" : "Name");
                if (shape is "wrong-name-type" or "wrong-name-value") payload.WriteInt32(3);
                else payload.WriteSerializedString(name);
            }
        }
        if (shape != "absent") metadata.AddCustomAttribute(method, constructor, metadata.GetOrAddBlob(payload));
        if (shape == "duplicate") metadata.AddCustomAttribute(method, constructor, metadata.GetOrAddBlob(payload));
        if (shape is "translation" or "translation-malformed" or "translation-truncated" or "unrelated-parameter")
        {
            // UnsafeAccessorTypeAttribute(string typeName); the malformed shape gives it an integer instead.
            var translation = metadata.AddTypeReference(module, metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString(shape == "unrelated-parameter" ? "OtherAttribute" : "UnsafeAccessorTypeAttribute"));
            var translationSignature = new BlobBuilder();
            new BlobEncoder(translationSignature).MethodSignature(isInstanceMethod: true).Parameters(1, result => result.Void(),
                parameters =>
                {
                    if (shape == "translation-malformed") parameters.AddParameter().Type().Int32();
                    else parameters.AddParameter().Type().String();
                });
            var translationConstructor = metadata.AddMemberReference(translation, metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(translationSignature));
            var translationPayload = new BlobBuilder();
            translationPayload.WriteUInt16(1);
            if (shape == "translation-malformed") translationPayload.WriteInt32(3);
            else if (shape != "translation-truncated") translationPayload.WriteSerializedString(TranslatedType);
            if (shape != "translation-truncated") translationPayload.WriteUInt16(0);
            metadata.AddCustomAttribute(parameter, translationConstructor, metadata.GetOrAddBlob(translationPayload));
        }
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        return new(MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray()), method);
    }

    private sealed class Fixture(MetadataReaderProvider provider, MethodDefinitionHandle method) : IDisposable
    {
        public MetadataReader Reader { get; } = provider.GetMetadataReader();
        public MethodDefinitionHandle Method { get; } = method;
        public void Dispose() => provider.Dispose();
    }
}
