// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedAssemblyLiteralTests
{
    [Fact]
    public void ParsePreservesEveryIntegerLiteralAndLeavesNonintegerLiteralsUnmapped()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("Constants"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("Constants");
        var owner = module.DefineType("Fixture.Constants", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        (string Name, Type Type, object? Value, ulong? Raw)[] literals =
        [
            ("Boolean", typeof(bool), true, 1),
            ("Byte", typeof(byte), byte.MaxValue, byte.MaxValue),
            ("Char", typeof(char), 'x', 'x'),
            ("Int16", typeof(short), short.MinValue, 0x8000),
            ("UInt16", typeof(ushort), ushort.MaxValue, ushort.MaxValue),
            ("SByte", typeof(sbyte), (sbyte)-1, byte.MaxValue),
            ("Int32", typeof(int), int.MinValue, 0x80000000),
            ("UInt32", typeof(uint), uint.MaxValue, uint.MaxValue),
            ("Int64", typeof(long), long.MinValue, 0x8000000000000000),
            ("UInt64", typeof(ulong), ulong.MaxValue, ulong.MaxValue),
            ("Single", typeof(float), 1.5f, null),
            ("Double", typeof(double), 1.5d, null),
            ("String", typeof(string), "literal", null),
            ("Null", typeof(object), null, null),
        ];
        foreach (var literal in literals)
            owner.DefineField(literal.Name, literal.Type, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal)
                .SetConstant(literal.Value);
        _ = owner.CreateType();
        using var image = new MemoryStream();
        builder.Save(image);

        using var parsed = Parse(image.ToArray());

        Assert.Equal(literals.Length, parsed.Fields.Count);
        foreach (var literal in literals)
            Assert.Equal(literal.Raw, Assert.Single(parsed.Fields.Values, field => field.Name == literal.Name).LiteralValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseRejectsTruncatedAndTrailingLiteralDataAsMetadataFailures(bool trailing)
    {
        var image = CreateEnumImage(PrimitiveTypeCode.Int32, true);
        using (var pe = new PEReader(new MemoryStream(image, writable: false)))
        {
            var reader = pe.GetMetadataReader();
            var constant = reader.GetConstant(reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(2)).GetDefaultValue());
            var blobOffset = pe.PEHeaders.MetadataStartOffset + reader.GetHeapMetadataOffset(HeapIndex.Blob) +
                MetadataTokens.GetHeapOffset(constant.Value);
            image[blobOffset] = (byte)(trailing ? sizeof(int) + 1 : sizeof(int) - 1);
        }

        var failure = Assert.Throws<CompilerException>(() => Parse(image));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, failure.Diagnostic.Code);
        Assert.Contains(trailing ? "trailing literal bytes" : "truncated metadata constant", failure.Message);
    }

    [Theory]
    [InlineData(PrimitiveTypeCode.Int32, false, "no metadata constant")]
    [InlineData(PrimitiveTypeCode.Single, true, "unsupported underlying type")]
    public void ParseRejectsMissingEnumConstantsAndUnsupportedUnderlyingTypes(
        PrimitiveTypeCode underlying, bool constant, string expected)
    {
        var failure = Assert.Throws<CompilerException>(() => Parse(CreateEnumImage(underlying, constant)));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, failure.Diagnostic.Code);
        Assert.Contains(expected, failure.Message);
    }

    [Fact]
    public void ParsePreservesTheRecordedWidthOfCharacterEnumMetadata()
    {
        using var parsed = Parse(CreateEnumImage(PrimitiveTypeCode.Char, true));

        var choice = Assert.Single(parsed.Types.Values, type => type.Name == "Choice");
        Assert.True(choice.IsEnum);
        Assert.Equal("primitive:char", choice.EnumUnderlyingType.CanonicalName);
        Assert.Equal(ushort.MaxValue, Assert.Single(choice.EnumMembers).RawValue);
    }

    [Theory]
    [InlineData(PrimitiveTypeCode.SByte, byte.MaxValue)]
    [InlineData(PrimitiveTypeCode.Byte, byte.MaxValue)]
    [InlineData(PrimitiveTypeCode.Int16, ushort.MaxValue)]
    [InlineData(PrimitiveTypeCode.UInt16, ushort.MaxValue)]
    [InlineData(PrimitiveTypeCode.Int32, uint.MaxValue)]
    [InlineData(PrimitiveTypeCode.UInt32, uint.MaxValue)]
    [InlineData(PrimitiveTypeCode.Int64, ulong.MaxValue)]
    [InlineData(PrimitiveTypeCode.UInt64, ulong.MaxValue)]
    public void ParseNormalizesEveryIntegerEnumWidth(PrimitiveTypeCode underlying, ulong expected)
    {
        object value = underlying switch
        {
            PrimitiveTypeCode.SByte => (sbyte)-1,
            PrimitiveTypeCode.Byte => byte.MaxValue,
            PrimitiveTypeCode.Int16 => (short)-1,
            PrimitiveTypeCode.UInt16 => ushort.MaxValue,
            PrimitiveTypeCode.Int32 => -1,
            PrimitiveTypeCode.UInt32 => uint.MaxValue,
            PrimitiveTypeCode.Int64 => -1L,
            _ => ulong.MaxValue,
        };

        using var parsed = Parse(CreateEnumImage(underlying, true, value));

        var choice = Assert.Single(parsed.Types.Values, type => type.Name == "Choice");
        Assert.True(choice.IsEnum);
        Assert.Equal(expected, Assert.Single(choice.EnumMembers).RawValue);
    }

    [Fact]
    public void ParseRejectsAnAbsentNativeReaderBeforeReadingTheImage()
    {
        var failure = Assert.Throws<ArgumentNullException>(() => ManagedAssembly.Parse(
            "memory/constants.dll", [], AssemblyIdentityAliases.Empty, new StackKinds(), null!));

        Assert.Equal("nativeImports", failure.ParamName);
    }

    private static ManagedAssembly Parse(byte[] image) => ManagedAssembly.Parse(
        "memory/constants.dll", image, AssemblyIdentityAliases.Empty, new StackKinds(), new NoNativeImports());

    private static byte[] CreateEnumImage(PrimitiveTypeCode underlying, bool hasConstant, object? value = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Constants.dll"),
            metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Constants"), new(1, 0), default, default, 0, 0);
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new(10, 0),
            default, default, 0, default);
        var enumBase = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("Enum"));
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var enumType = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Fixture"), metadata.GetOrAddString("Choice"), enumBase,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var backingSignature = new BlobBuilder();
        new BlobEncoder(backingSignature).FieldSignature().PrimitiveType(underlying);
        metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"), metadata.GetOrAddBlob(backingSignature));
        var memberSignature = new BlobBuilder();
        new BlobEncoder(memberSignature).FieldSignature().Type(enumType, isValueType: true);
        var member = metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal,
            metadata.GetOrAddString("Value"), metadata.GetOrAddBlob(memberSignature));
        if (hasConstant)
            metadata.AddConstant(member, value ?? (underlying == PrimitiveTypeCode.Char ? (object)(char)ushort.MaxValue : 1));
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
    }

    private sealed class StackKinds : IValueTypeDefinitionStackKindResolver
    {
        public CliValueKind Resolve(string canonicalName) => CliValueKind.ValueType;
    }

    private sealed class NoNativeImports : INativeImportDeclarationReader
    {
        public NativeImportDeclaration? Read(MetadataReader metadata, MethodDefinition method) => null;
    }
}
