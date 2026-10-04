using System;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

internal sealed class CustomAttributeTypeNameProvider :
    ICustomAttributeTypeProvider<string>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        typeCode.ToString();

    public string GetSystemType() => "System.Type";

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        return Name(reader, type.Namespace, type.Name);
    }

    public string GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return Name(reader, type.Namespace, type.Name);
    }

    public string GetTypeFromSerializedName(string name) => name;

    public PrimitiveTypeCode GetUnderlyingEnumType(string type) =>
        PrimitiveTypeCode.Int32;

    public bool IsSystemType(string type) => type == "System.Type";

    public static string NormalizeSerializedTypeName(string name)
    {
        var separator = name.IndexOf(',');
        return (separator < 0 ? name : name[..separator]).Trim();
    }

    private static string Name(
        MetadataReader reader,
        StringHandle typeNamespace,
        StringHandle typeName) =>
        reader.GetString(typeNamespace) + "." + reader.GetString(typeName);
}
