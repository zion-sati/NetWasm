using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class JSTypeMarkerProviderTests
{
    private const string InteropNamespace = "System.Runtime.InteropServices.JavaScript";

    [Fact]
    public void NamesJSTypeMarkersByThemselvesAndOtherTypesByTheirMetadataNames()
    {
        var metadata = new MetadataBuilder();
        var module = metadata.AddModule(
            0, metadata.GetOrAddString("Markers"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        Define(metadata, TypeAttributes.NotPublic, "", "<Module>");
        var jsType = Define(metadata, TypeAttributes.Public | TypeAttributes.Abstract, InteropNamespace, "JSType");
        var promise = Define(metadata, TypeAttributes.NestedPublic | TypeAttributes.Sealed, "", "Promise`1");
        var holder = Define(metadata, TypeAttributes.Public, "Probe", "Holder");
        var inner = Define(metadata, TypeAttributes.NestedPublic, "", "Inner");
        var topLevel = Define(metadata, TypeAttributes.Public, "", "TopLevel");
        metadata.AddNestedType(promise, jsType);
        metadata.AddNestedType(inner, holder);
        var jsTypeReference = metadata.AddTypeReference(
            module, metadata.GetOrAddString(InteropNamespace), metadata.GetOrAddString("JSType"));
        var number = metadata.AddTypeReference(jsTypeReference, default, metadata.GetOrAddString("Number"));
        var holderReference = metadata.AddTypeReference(
            module, metadata.GetOrAddString("Probe"), metadata.GetOrAddString("Holder"));
        var innerReference = metadata.AddTypeReference(holderReference, default, metadata.GetOrAddString("Inner"));
        var outside = metadata.AddTypeReference(module, default, metadata.GetOrAddString("Outside"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).TypeSpecificationSignature()
            .GenericInstantiation(holderReference, 1, isValueType: false)
            .AddArgument().Int32();
        var specification = metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using var owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var reader = owner.GetMetadataReader();
        var provider = new JSTypeMarkerProvider(reader);

        Assert.Equal("Promise", provider.GetTypeFromDefinition(reader, promise, 0));
        Assert.Equal("Probe.Holder+Inner", provider.GetTypeFromDefinition(reader, inner, 0));
        Assert.Equal("TopLevel", provider.GetTypeFromDefinition(reader, topLevel, 0));
        Assert.Equal("Number", provider.GetTypeFromReference(reader, number, 0));
        Assert.Equal("Probe.Holder+Inner", provider.GetTypeFromReference(reader, innerReference, 0));
        Assert.Equal("Outside", provider.GetTypeFromReference(reader, outside, 0));
        Assert.Equal("Probe.Holder<Int32>", provider.GetTypeFromSpecification(reader, null, specification, 0));
    }

    [Fact]
    public void RendersEveryOtherSignatureShapeAsANameThatIsNoMarker()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Shapes"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using var owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var provider = new JSTypeMarkerProvider(owner.GetMetadataReader());

        Assert.Equal("Int32", provider.GetPrimitiveType(PrimitiveTypeCode.Int32));
        Assert.Equal("Promise<Number,String>", provider.GetGenericInstantiation("Promise", ["Number", "String"]));
        Assert.Equal("Number[]", provider.GetSZArrayType("Number"));
        Assert.Equal("Number[*]", provider.GetArrayType("Number", new ArrayShape(2, [], [])));
        Assert.Equal("Number&", provider.GetByReferenceType("Number"));
        Assert.Equal("Number*", provider.GetPointerType("Number"));
        Assert.Equal("Number", provider.GetPinnedType("Number"));
        Assert.Equal("Number", provider.GetModifiedType("Modifier", "Number", isRequired: true));
        Assert.Equal("method", provider.GetFunctionPointerType(default));
        Assert.Equal("!!1", provider.GetGenericMethodParameter(null, 1));
        Assert.Equal("!0", provider.GetGenericTypeParameter(null, 0));
    }

    private static TypeDefinitionHandle Define(
        MetadataBuilder metadata,
        TypeAttributes attributes,
        string @namespace,
        string name) => metadata.AddTypeDefinition(
            attributes,
            @namespace.Length == 0 ? default : metadata.GetOrAddString(@namespace),
            metadata.GetOrAddString(name),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
}
