// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeTypeNameProviderTests
{
    [Fact]
    public void ProjectsDefinitionReferenceAndIntrinsicNamesWithoutReflection()
    {
        var metadata = new MetadataBuilder();
        var module = metadata.AddModule(
            0,
            metadata.GetOrAddString("AttributeNames"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var definition = metadata.AddTypeDefinition(
            System.Reflection.TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("LocalType"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var reference = metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("External"),
            metadata.GetOrAddString("ReferencedType"));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);

        using var owner = MetadataReaderProvider.FromMetadataImage(
            image.ToImmutableArray());
        var reader = owner.GetMetadataReader();
        var provider = new CustomAttributeTypeNameProvider();

        Assert.Equal("Int32", provider.GetPrimitiveType(PrimitiveTypeCode.Int32));
        Assert.Equal("System.Type", provider.GetSystemType());
        Assert.Equal("System.Type[]", provider.GetSZArrayType("System.Type"));
        Assert.Equal("Fixtures.LocalType",
            provider.GetTypeFromDefinition(reader, definition, 0));
        Assert.Equal("External.ReferencedType",
            provider.GetTypeFromReference(reader, reference, 0));
        Assert.Equal("Serialized", provider.GetTypeFromSerializedName("Serialized"));
        Assert.Equal(PrimitiveTypeCode.Int32,
            provider.GetUnderlyingEnumType("Fixtures.Enum"));
        Assert.True(provider.IsSystemType("System.Type"));
        Assert.False(provider.IsSystemType("Fixtures.Type"));
        Assert.Equal("Fixtures.Type",
            CustomAttributeTypeNameProvider.NormalizeSerializedTypeName(
                " Fixtures.Type, Fixtures "));
        Assert.Equal("Fixtures.Type",
            CustomAttributeTypeNameProvider.NormalizeSerializedTypeName(
                " Fixtures.Type "));
    }
}
