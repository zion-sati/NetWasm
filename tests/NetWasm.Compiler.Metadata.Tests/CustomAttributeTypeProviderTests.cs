// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeTypeProviderTests
{
    private static readonly AssemblyIdentity Assembly = new("AttributeTypes");
    private static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(Assembly, "System", "Type", false);
    private static readonly CliTypeIdentity EnumType = CliTypeIdentity.Named(Assembly, "Test", "Code", true);

    [Fact]
    public void ProtocolCallbacksPreserveIdentitiesAndSourceContext()
    {
        using var fixture = new Fixture();
        var provider = fixture.Create();
        Assert.Same(SystemType, provider.GetSystemType());
        Assert.True(provider.IsSystemType(SystemType));
        Assert.False(provider.IsSystemType(EnumType));
        Assert.Equal(CliTypeIdentity.SzArray(EnumType), provider.GetSZArrayType(EnumType));
        Assert.Same(fixture.Primitive, provider.GetPrimitiveType(PrimitiveTypeCode.Int32));
        Assert.Equal([PrimitiveTypeCode.Int32], fixture.PrimitiveRequests);
        var definition = MetadataTokens.TypeDefinitionHandle(1);
        var reference = MetadataTokens.TypeReferenceHandle(2);
        Assert.Same(EnumType, provider.GetTypeFromDefinition(fixture.Source.Reader, definition, 0));
        Assert.Same(EnumType, provider.GetTypeFromReference(fixture.Source.Reader, reference, 0));
        Assert.Equal([(EntityHandle)definition, reference], fixture.SignatureRequests);
        Assert.Same(EnumType, provider.GetTypeFromSerializedName("Test.Code"));
        Assert.Null(provider.GetTypeFromSerializedName(null!));
        Assert.Equal(["Test.Code", null], fixture.NameRequests);
    }

    [Theory]
    [InlineData("i1", PrimitiveTypeCode.SByte)]
    [InlineData("u1", PrimitiveTypeCode.Byte)]
    [InlineData("i2", PrimitiveTypeCode.Int16)]
    [InlineData("u2", PrimitiveTypeCode.UInt16)]
    [InlineData("i4", PrimitiveTypeCode.Int32)]
    [InlineData("u4", PrimitiveTypeCode.UInt32)]
    [InlineData("i8", PrimitiveTypeCode.Int64)]
    [InlineData("u8", PrimitiveTypeCode.UInt64)]
    public void EnumStorageComesFromTheResolvedDefinition(string storage, PrimitiveTypeCode expected)
    {
        using var fixture = new Fixture { Storage = storage };
        Assert.Equal(expected, fixture.Create().GetUnderlyingEnumType(EnumType));
    }

    [Theory]
    [InlineData(false, "i4")]
    [InlineData(true, "f4")]
    public void NonEnumsAndInvalidEnumStorageAreMalformedPayloads(bool isEnum, string storage)
    {
        using var fixture = new Fixture { IsEnum = isEnum, Storage = storage };
        Assert.Throws<BadImageFormatException>(() => fixture.Create().GetUnderlyingEnumType(EnumType));
    }

    private sealed class Fixture : IDisposable, ISimpleTypeProvider<CliTypeIdentity>,
        IMetadataSignatureTypeResolver, ISerializedTypeNameResolver, ITypeDefinitionResolver
    {
        private readonly MetadataReaderProvider _owner;
        internal Fixture()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("Types"), default, default, default);
            var image = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
            _owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
            Source = new(Assembly, _owner.GetMetadataReader(),
                new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
                new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>());
        }

        internal MetadataAssemblySnapshot Source { get; }
        internal CliTypeIdentity Primitive { get; } = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        internal bool IsEnum { get; init; } = true;
        internal string Storage { get; init; } = "i4";
        internal List<PrimitiveTypeCode> PrimitiveRequests { get; } = [];
        internal List<EntityHandle> SignatureRequests { get; } = [];
        internal List<string?> NameRequests { get; } = [];
        internal CustomAttributeTypeProvider Create() => new(Source, this, this, this, this, SystemType);

        public CliTypeIdentity GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            PrimitiveRequests.Add(typeCode);
            return Primitive;
        }

        public CliTypeIdentity GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle,
            byte rawTypeKind) => throw new NotSupportedException();

        public CliTypeIdentity GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle,
            byte rawTypeKind) => throw new NotSupportedException();

        public CliTypeIdentity Resolve(MetadataAssemblySnapshot source, EntityHandle handle,
            CliGenericContext? genericContext = null)
        {
            Assert.Same(Source, source);
            Assert.Null(genericContext);
            SignatureRequests.Add(handle);
            return EnumType;
        }

        public CliTypeIdentity? Resolve(string? name, MetadataAssemblySnapshot source)
        {
            Assert.Same(Source, source);
            NameRequests.Add(name);
            return name is null ? null : EnumType;
        }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Assert.Same(EnumType, identity);
            return new(new(Assembly, 0x02000001), "Test", "Code", true, [], [])
            {
                IsEnum = IsEnum,
                EnumUnderlyingType = CliTypeIdentity.Primitive(Storage,
                    Storage is "i8" or "u8" ? CliValueKind.I8 : CliValueKind.I4),
            };
        }

        public void Dispose() => _owner.Dispose();
    }
}
