using System;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Adapter for SRM's custom-attribute decoding protocol. Definition resolution
// supplies actual enum/value-type identity even when SRM passes rawTypeKind 0.
internal sealed class CustomAttributeTypeProvider(
    MetadataAssemblySnapshot source,
    ISimpleTypeProvider<CliTypeIdentity> primitives,
    IMetadataSignatureTypeResolver signatures,
    ISerializedTypeNameResolver names,
    ITypeDefinitionResolver definitions,
    CliTypeIdentity systemType) : ICustomAttributeTypeProvider<CliTypeIdentity>
{
    public CliTypeIdentity GetPrimitiveType(PrimitiveTypeCode typeCode) => primitives.GetPrimitiveType(typeCode);

    public CliTypeIdentity GetSystemType() => systemType;

    public bool IsSystemType(CliTypeIdentity type) => type.Equals(systemType);

    public CliTypeIdentity GetSZArrayType(CliTypeIdentity elementType) => CliTypeIdentity.SzArray(elementType);

    public CliTypeIdentity GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle,
        byte rawTypeKind) => signatures.Resolve(source, handle);

    public CliTypeIdentity GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle,
        byte rawTypeKind) => signatures.Resolve(source, handle);

    // SRM calls this with null for a null System.Type payload.
    public CliTypeIdentity GetTypeFromSerializedName(string name) => names.Resolve(name, source)!;

    public PrimitiveTypeCode GetUnderlyingEnumType(CliTypeIdentity type)
    {
        var definition = definitions.ResolveTypeIdentity(type);
        if (!definition.IsEnum) throw new BadImageFormatException("Attribute payload type is not an enum.");
        return definition.EnumUnderlyingType.CanonicalName switch
        {
            "primitive:i1" => PrimitiveTypeCode.SByte,
            "primitive:u1" => PrimitiveTypeCode.Byte,
            "primitive:i2" => PrimitiveTypeCode.Int16,
            "primitive:u2" => PrimitiveTypeCode.UInt16,
            "primitive:i4" => PrimitiveTypeCode.Int32,
            "primitive:u4" => PrimitiveTypeCode.UInt32,
            "primitive:i8" => PrimitiveTypeCode.Int64,
            "primitive:u8" => PrimitiveTypeCode.UInt64,
            _ => throw new BadImageFormatException("Attribute enum has invalid underlying storage."),
        };
    }
}
