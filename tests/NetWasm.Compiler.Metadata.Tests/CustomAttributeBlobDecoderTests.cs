// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeBlobDecoderTests
{
    public static TheoryData<PrimitiveTypeCode, object> Scalars => new()
    {
        { PrimitiveTypeCode.Boolean, true }, { PrimitiveTypeCode.Char, 'λ' },
        { PrimitiveTypeCode.SByte, (sbyte)-7 }, { PrimitiveTypeCode.Byte, (byte)241 },
        { PrimitiveTypeCode.Int16, (short)-31000 }, { PrimitiveTypeCode.UInt16, (ushort)62000 },
        { PrimitiveTypeCode.Int32, int.MinValue }, { PrimitiveTypeCode.UInt32, uint.MaxValue },
        { PrimitiveTypeCode.Int64, long.MinValue }, { PrimitiveTypeCode.UInt64, ulong.MaxValue },
        { PrimitiveTypeCode.Single, 1.25f }, { PrimitiveTypeCode.Double, -3.5 },
        { PrimitiveTypeCode.String, "payload" },
    };

    [Theory]
    [MemberData(nameof(Scalars))]
    public void PreservesPrimitiveIdentityAndValue(PrimitiveTypeCode code, object value)
    {
        var fixture = new Fixture();
        var type = fixture.GetPrimitiveType(code);
        var result = fixture.Decode(blob => Write(blob, value), [type]);

        var argument = Assert.Single(result.FixedArguments);
        Assert.Equal(type, argument.Type);
        Assert.Equal(value, argument.Value);
        Assert.Empty(result.NamedArguments);
    }

    [Theory]
    [MemberData(nameof(Scalars))]
    public void NamedFieldsPreservePrimitiveIdentityAndValue(PrimitiveTypeCode code, object value)
    {
        var fixture = new Fixture();
        var result = fixture.Decode(_ => { }, [], named: blob =>
        {
            blob.WriteByte((byte)CustomAttributeNamedArgumentKind.Field);
            blob.WriteByte((byte)code);
            blob.WriteSerializedString("Value");
            Write(blob, value);
        });
        Assert.Empty(result.FixedArguments);
        var argument = Assert.Single(result.NamedArguments);
        Assert.Equal("Value", argument.Name);
        Assert.Equal(CustomAttributeNamedArgumentKind.Field, argument.Kind);
        Assert.Equal(fixture.GetPrimitiveType(code), argument.Type);
        Assert.Equal(value, argument.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedTypePropertiesPreserveNullAndResolvedValues(bool absent)
    {
        var fixture = new Fixture();
        var result = fixture.Decode(_ => { }, [], named: blob =>
        {
            blob.WriteByte((byte)CustomAttributeNamedArgumentKind.Property);
            blob.WriteByte((byte)SerializationTypeCode.Type);
            blob.WriteSerializedString("Value");
            blob.WriteSerializedString(absent ? null : "Enum");
        });
        var argument = Assert.Single(result.NamedArguments);
        Assert.Equal(CustomAttributeNamedArgumentKind.Property, argument.Kind);
        Assert.Equal("Value", argument.Name);
        Assert.Equal(Fixture.SystemType, argument.Type);
        Assert.Equal(absent ? null : Fixture.Enum, argument.Value);
    }

    [Fact]
    public void NamedObjectPropertiesRetainBoxedEnumVectorIdentityAndStorage()
    {
        var fixture = new Fixture();
        var result = fixture.Decode(_ => { }, [], named: blob =>
        {
            blob.WriteByte((byte)CustomAttributeNamedArgumentKind.Property);
            blob.WriteByte((byte)SerializationTypeCode.TaggedObject);
            blob.WriteSerializedString("Value");
            blob.WriteByte((byte)SerializationTypeCode.SZArray);
            blob.WriteByte((byte)SerializationTypeCode.Enum);
            blob.WriteSerializedString("Enum");
            blob.WriteInt32(1);
            blob.WriteUInt16(42);
        });
        var argument = Assert.Single(result.NamedArguments);
        Assert.Equal(CustomAttributeNamedArgumentKind.Property, argument.Kind);
        Assert.Equal("Value", argument.Name);
        Assert.Equal(CliTypeShape.SzArray, argument.Type.Shape);
        Assert.Equal(Fixture.Enum.CanonicalName, argument.Type.ElementType!.CanonicalName);
        Assert.Equal("primitive:u2", argument.Type.ElementType.StackStorageType!.CanonicalName);
        var element = Assert.Single(Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>>(argument.Value));
        Assert.Equal(argument.Type.ElementType, element.Type);
        Assert.Equal((ushort)42, Assert.IsType<ushort>(element.Value));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("type")]
    [InlineData("jagged")]
    [InlineData("recursive-object")]
    public void MalformedNamedArgumentsAreRejected(string shape)
    {
        Assert.Throws<BadImageFormatException>(() => new Fixture().Decode(_ => { }, [], named: blob =>
        {
            blob.WriteByte(shape == "kind" ? (byte)0 : (byte)CustomAttributeNamedArgumentKind.Field);
            blob.WriteByte(shape switch
            {
                "jagged" => (byte)SerializationTypeCode.SZArray,
                "recursive-object" => (byte)SerializationTypeCode.TaggedObject,
                _ => 0,
            });
            if (shape == "jagged") blob.WriteByte((byte)SerializationTypeCode.SZArray);
            blob.WriteSerializedString("Value");
            if (shape == "recursive-object") blob.WriteByte((byte)SerializationTypeCode.TaggedObject);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvesTypeValuesAndPreservesNull(bool absent)
    {
        var fixture = new Fixture();
        var value = Assert.Single(fixture.Decode(blob => blob.WriteSerializedString(absent ? null : "Enum"),
            [Fixture.SystemType]).FixedArguments);
        Assert.Equal(Fixture.SystemType, value.Type);
        Assert.Equal(absent ? null : Fixture.Enum, value.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizesNestedEnumStorageForScalarsAndVectors(bool vector)
    {
        var fixture = new Fixture();
        var parameter = vector ? CliTypeIdentity.SzArray(Fixture.Enum) : Fixture.Enum;
        var argument = Assert.Single(fixture.Decode(blob =>
        {
            if (vector) blob.WriteInt32(1);
            blob.WriteUInt16(42);
        }, [parameter]).FixedArguments);
        if (vector)
        {
            Assert.Equal(CliTypeShape.SzArray, argument.Type.Shape);
            Assert.Equal("primitive:u2", argument.Type.ElementType!.StackStorageType!.CanonicalName);
            argument = Assert.Single(Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>>(argument.Value));
        }
        Assert.Equal(Fixture.Enum.CanonicalName, argument.Type.CanonicalName);
        Assert.Equal("primitive:u2", argument.Type.StackStorageType!.CanonicalName);
        Assert.Equal((ushort)42, Assert.IsType<ushort>(argument.Value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void DistinguishesNullAndEmptyVectors(int count)
    {
        var fixture = new Fixture();
        var argument = Assert.Single(fixture.Decode(blob => blob.WriteInt32(count),
            [CliTypeIdentity.SzArray(Fixture.Enum)]).FixedArguments);
        if (count == -1) Assert.Null(argument.Value);
        else Assert.Empty(Assert.IsType<ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>>(argument.Value));
    }

    [Fact]
    public void BoxedEnumsRetainTheirConcreteIdentity()
    {
        var fixture = new Fixture();
        var argument = Assert.Single(fixture.Decode(blob =>
        {
            blob.WriteByte((byte)SerializationTypeCode.Enum);
            blob.WriteSerializedString("Enum");
            blob.WriteUInt16(42);
        }, [fixture.GetPrimitiveType(PrimitiveTypeCode.Object)]).FixedArguments);
        Assert.Equal(Fixture.Enum.CanonicalName, argument.Type.CanonicalName);
        Assert.Equal("primitive:u2", argument.Type.StackStorageType!.CanonicalName);
        Assert.Equal((ushort)42, Assert.IsType<ushort>(argument.Value));
    }

    [Fact]
    public void RejectsNullEnumNamesAsMalformedMetadata()
    {
        var fixture = new Fixture();
        Assert.Throws<BadImageFormatException>(() => fixture.Decode(blob =>
        {
            blob.WriteByte((byte)SerializationTypeCode.Enum);
            blob.WriteSerializedString(null);
        }, [fixture.GetPrimitiveType(PrimitiveTypeCode.Object)]));
    }

    [Fact]
    public void RejectsTrailingBytesAfterACompletePayload()
    {
        Assert.Throws<BadImageFormatException>(() => new Fixture().Decode(_ => { }, [], trailing: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsMalformedValuesAndUnsupportedParameterShapes(int shape)
    {
        var fixture = new Fixture();
        var integer = fixture.GetPrimitiveType(PrimitiveTypeCode.Int32);
        var type = shape switch
        {
            2 => CliTypeIdentity.SzArray(integer),
            3 => CliTypeIdentity.SzArray(CliTypeIdentity.SzArray(integer)),
            4 => CliTypeIdentity.UnmanagedPointer(integer),
            _ => integer,
        };
        Assert.Throws<BadImageFormatException>(() => fixture.Decode(blob =>
        {
            if (shape == 2) blob.WriteInt32(-2);
        }, [type], validProlog: shape != 0));
    }

    private static void Write(BlobBuilder blob, object value)
    {
        switch (value)
        {
            case bool item: blob.WriteBoolean(item); break;
            case char item: blob.WriteUInt16(item); break;
            case sbyte item: blob.WriteSByte(item); break;
            case byte item: blob.WriteByte(item); break;
            case short item: blob.WriteInt16(item); break;
            case ushort item: blob.WriteUInt16(item); break;
            case int item: blob.WriteInt32(item); break;
            case uint item: blob.WriteUInt32(item); break;
            case long item: blob.WriteInt64(item); break;
            case ulong item: blob.WriteUInt64(item); break;
            case float item: blob.WriteSingle(item); break;
            case double item: blob.WriteDouble(item); break;
            case string item: blob.WriteSerializedString(item); break;
            default: throw new ArgumentException("Unexpected scalar fixture.", nameof(value));
        }
    }

    private sealed class Fixture : ICustomAttributeTypeProvider<CliTypeIdentity>, IMetadataStackTypeResolver
    {
        internal static readonly CliTypeIdentity Enum = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new("Test"), "Test", "Outer`1+Code", true),
            [CliTypeIdentity.FromStackKind(CliValueKind.I4)]);
        internal static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(new("Core"), "System", "Type", false);
        private readonly SignatureTypeProvider _primitives = new(new("Core"));

        internal CustomAttributeValue<CliTypeIdentity> Decode(Action<BlobBuilder> write,
            ImmutableArray<CliTypeIdentity> parameters, bool validProlog = true, bool trailing = false,
            Action<BlobBuilder>? named = null)
        {
            var value = new BlobBuilder();
            value.WriteUInt16(validProlog ? (ushort)1 : (ushort)0);
            write(value);
            value.WriteUInt16(named is null ? (ushort)0 : (ushort)1);
            named?.Invoke(value);
            if (trailing) value.WriteByte(1);
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("AttributeBlob"), default, default, default);
            var handle = metadata.GetOrAddBlob(value);
            var image = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
            using var owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
            return ((ICustomAttributeBlobDecoder)new CustomAttributeBlobDecoder(this, this))
                .Decode(owner.GetMetadataReader().GetBlobReader(handle), parameters);
        }

        public CliTypeIdentity GetPrimitiveType(PrimitiveTypeCode typeCode) => _primitives.GetPrimitiveType(typeCode);
        public CliTypeIdentity GetSystemType() => SystemType;
        public bool IsSystemType(CliTypeIdentity type) => type.Equals(SystemType);
        public CliTypeIdentity GetSZArrayType(CliTypeIdentity elementType) => CliTypeIdentity.SzArray(elementType);
        public CliTypeIdentity GetTypeFromSerializedName(string name) => name is null ? null! : Enum;
        public PrimitiveTypeCode GetUnderlyingEnumType(CliTypeIdentity type) => type.Equals(Enum)
            ? PrimitiveTypeCode.UInt16 : throw new BadImageFormatException();
        public CliTypeIdentity Resolve(CliTypeIdentity type) => type.Equals(Enum)
            ? type.WithStackStorageType(GetPrimitiveType(PrimitiveTypeCode.UInt16)) : type;
        public CliTypeIdentity GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            throw new NotSupportedException();
        public CliTypeIdentity GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            throw new NotSupportedException();
        public CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext) => throw new NotSupportedException();
        public MethodSignatureModel Resolve(MethodSignatureModel signature) => throw new NotSupportedException();
        public MethodSignatureModel Resolve(MethodSignatureModel signature, CliGenericContext genericContext) => throw new NotSupportedException();
    }
}
