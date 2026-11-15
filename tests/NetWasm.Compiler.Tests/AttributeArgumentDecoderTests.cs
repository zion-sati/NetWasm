// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeArgumentDecoderTests
{
    private static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(new("Core"), "System", "Type", false);
    private static readonly CliTypeIdentity ObjectType = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    public static TheoryData<string, CliValueKind, object, AttributeArgumentKind> Scalars => new()
    {
        { "bool", CliValueKind.I4, true, AttributeArgumentKind.Boolean },
        { "bool", CliValueKind.I4, false, AttributeArgumentKind.Boolean },
        { "char", CliValueKind.I4, 'z', AttributeArgumentKind.Character },
        { "i1", CliValueKind.I4, (sbyte)-7, AttributeArgumentKind.SignedInteger },
        { "i2", CliValueKind.I4, (short)-7, AttributeArgumentKind.SignedInteger },
        { "i4", CliValueKind.I4, -7, AttributeArgumentKind.SignedInteger },
        { "i8", CliValueKind.I8, -7L, AttributeArgumentKind.SignedInteger },
        { "u1", CliValueKind.I4, (byte)7, AttributeArgumentKind.UnsignedInteger },
        { "u2", CliValueKind.I4, (ushort)7, AttributeArgumentKind.UnsignedInteger },
        { "u4", CliValueKind.I4, 7U, AttributeArgumentKind.UnsignedInteger },
        { "u8", CliValueKind.I8, ulong.MaxValue, AttributeArgumentKind.UnsignedInteger },
        { "f4", CliValueKind.F4, 1.25F, AttributeArgumentKind.Floating32 },
        { "f8", CliValueKind.F8, 1.25D, AttributeArgumentKind.Floating64 },
        { "string", CliValueKind.ManagedReference, "text", AttributeArgumentKind.Text },
    };

    [Theory]
    [MemberData(nameof(Scalars))]
    public void BoxedScalarsKeepTheirSerializedIdentityAndValue(string name, CliValueKind storage,
        object value, AttributeArgumentKind kind)
    {
        var type = CliTypeIdentity.Primitive(name, storage, storage != CliValueKind.ManagedReference);
        var result = Decode(new(type, value), ObjectType);

        Assert.Equal(ObjectType, result.Type);
        Assert.Equal(type, result.SerializedType);
        Assert.Equal(kind, result.Kind);
        switch (kind)
        {
            case AttributeArgumentKind.Boolean: Assert.Equal((bool)value ? 1 : 0, result.SignedValue); break;
            case AttributeArgumentKind.Character: Assert.Equal((ulong)'z', result.UnsignedValue); break;
            case AttributeArgumentKind.SignedInteger: Assert.Equal(-7, result.SignedValue); break;
            case AttributeArgumentKind.UnsignedInteger:
                Assert.Equal(Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture), result.UnsignedValue);
                break;
            case AttributeArgumentKind.Floating32:
            case AttributeArgumentKind.Floating64: Assert.Equal(1.25, result.FloatingValue); break;
            case AttributeArgumentKind.Text: Assert.Equal("text", result.StringValue); break;
            default: throw new InvalidOperationException();
        }
    }

    [Fact]
    public void BoxedEnumsAndTypesKeepSemanticIdentity()
    {
        var storage = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
        var enumeration = CliTypeIdentity.Named(new("Test"), "Test", "Number", true).WithStackStorageType(storage);
        var result = Decode(new(enumeration, ulong.MaxValue), ObjectType);
        Assert.Equal(enumeration, result.SerializedType);
        Assert.Equal(ulong.MaxValue, result.UnsignedValue);
        var type = CliTypeIdentity.SzArray(storage);
        Assert.Equal(type, Decode(new(SystemType, type), ObjectType).TypeValue);
        Assert.Equal(AttributeArgumentKind.Null, Decode(new(SystemType, null), SystemType).Kind);
    }

    [Fact]
    public void BoxedArraysPreserveElementDestinationsAndNullVersusEmpty()
    {
        var number = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var array = CliTypeIdentity.SzArray(ObjectType);
        ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>> elements =
            [new(number, 42), new(SystemType, number), new(ObjectType, null)];
        var result = Decode(new(array, elements), ObjectType);
        Assert.Equal(array, result.SerializedType);
        Assert.All(result.Elements, element => Assert.Equal(ObjectType, element.Type));
        Assert.Equal(42, result.Elements[0].SignedValue);
        Assert.Equal(number, result.Elements[1].TypeValue);
        Assert.Equal(AttributeArgumentKind.Null, result.Elements[2].Kind);
        Assert.Equal(AttributeArgumentKind.Null,
            Decode(new(array, default(ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>)), array).Kind);
        var empty = Decode(new(array, ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>.Empty), array);
        Assert.Equal(AttributeArgumentKind.Array, empty.Kind);
        Assert.Empty(empty.Elements);
    }

    [Fact]
    public void MismatchedAndInvalidArgumentsFailBeforeCilEmission()
    {
        var number = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        Assert.Throws<ArgumentNullException>(() => Decode(new(number, 42), null!));
        Assert.Throws<CompilerException>(() => Decode(new(number, 42), SystemType));
        Assert.Throws<CompilerException>(() => Decode(new(number, null), number));
        Assert.Throws<CompilerException>(() => Decode(new(number, "wrong"), number));
        Assert.Throws<CompilerException>(() => Decode(new(number,
            ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>.Empty), number));
    }

    private static AttributeArgumentPlan Decode(CustomAttributeTypedArgument<CliTypeIdentity> value,
        CliTypeIdentity destination) => ((IAttributeArgumentDecoder)new AttributeArgumentDecoder(SystemType))
            .Decode(value, destination);
}
