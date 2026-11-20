// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.Tests;

// A field's [MarshalAs] as its marshalling descriptor states it: one native type alone; a ByValTStr or ByValArray with
// its element count and, for an array, an element type when one is given; any other descriptor of more than its native
// type, or a fixed one whose count does not read (which C# never emits), is marshalling NetWasm does not express.
public sealed class ManagedAssemblyFieldMarshallingTests
{
    [Fact]
    public void AFieldsDescriptorGivesItsNativeTypeCountAndElementType()
    {
        using var assembly = ManagedAssembly.Parse("memory/marshal.dll", Image(), AssemblyIdentityAliases.Empty,
            new StackKinds(), new NoNativeImports());

        var fields = assembly.Fields.Values.ToDictionary(field => field.Name);

        Assert.Equal((UnmanagedType.U1, false), (fields["Plain"].MarshalAs, fields["Plain"].HasComplexMarshalAs));
        Assert.Equal((UnmanagedType.ByValArray, true, 4, UnmanagedType.U1), (fields["Typed"].MarshalAs,
            fields["Typed"].HasComplexMarshalAs, fields["Typed"].MarshalAsSizeConst, fields["Typed"].MarshalAsArraySubType));
        Assert.Equal((UnmanagedType.ByValArray, 3, null), (fields["Untyped"].MarshalAs, fields["Untyped"].MarshalAsSizeConst,
            fields["Untyped"].MarshalAsArraySubType));
        Assert.All(new[] { fields["Uncounted"], fields["Safe"] }, field =>
            Assert.Equal((null, true, null), (field.MarshalAs, field.HasComplexMarshalAs, field.MarshalAsSizeConst)));
        Assert.Equal((null, false), (fields["None"].MarshalAs, fields["None"].HasComplexMarshalAs));
    }

    private static byte[] Image()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Marshal.dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Marshal"), new(1, 0), default, default, 0, 0);
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new(10, 0),
            default, default, 0, default);
        var valueType = metadata.AddTypeReference(runtime, metadata.GetOrAddString("System"), metadata.GetOrAddString("ValueType"));
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            metadata.GetOrAddString("Fixture"), metadata.GetOrAddString("Layout"), valueType,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        Field("Plain", PrimitiveTypeCode.Boolean, [(byte)UnmanagedType.U1]);
        Field("Typed", PrimitiveTypeCode.Byte, [(byte)UnmanagedType.ByValArray, 4, (byte)UnmanagedType.U1], array: true);
        Field("Untyped", PrimitiveTypeCode.Int32, [(byte)UnmanagedType.ByValArray, 3], array: true);
        // A ByValTStr whose count does not read: 0xFF starts no compressed integer.
        Field("Uncounted", PrimitiveTypeCode.String, [(byte)UnmanagedType.ByValTStr, 0xFF]);
        // A SafeArray of VT_I4: a native type with more after it.
        Field("Safe", PrimitiveTypeCode.Int32, [(byte)UnmanagedType.SafeArray, 3], array: true);
        Field("None", PrimitiveTypeCode.Int32, null);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();

        void Field(string name, PrimitiveTypeCode type, byte[]? descriptor, bool array = false)
        {
            var signature = new BlobBuilder();
            var encoder = new BlobEncoder(signature).FieldSignature();
            if (array)
                encoder.SZArray().PrimitiveType(type);
            else
                encoder.PrimitiveType(type);
            var field = metadata.AddFieldDefinition(FieldAttributes.Public, metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature));
            if (descriptor is not null)
                metadata.AddMarshallingDescriptor(field, metadata.GetOrAddBlob(descriptor));
        }
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
