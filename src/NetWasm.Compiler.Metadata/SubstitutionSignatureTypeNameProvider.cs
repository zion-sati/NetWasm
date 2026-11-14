using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

// The signature decoder's Strategy for naming types as ILLink.Substitutions.xml writes them
// ("System.Diagnostics.Tracing.EventLevel"): primitives by their System type, nested types after a '/', and generic
// parameters by position, which a substitution naming them by name never matches.
internal sealed class SubstitutionSignatureTypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? Name(reader, type.Namespace, type.Name)
            : GetTypeFromDefinition(reader, declaring, rawTypeKind) + "/" + reader.GetString(type.Name);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "/" +
                reader.GetString(type.Name)
            : Name(reader, type.Namespace, type.Name);
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        "method " + signature.ReturnType + " *(" + string.Join(",", signature.ParameterTypes) + ")";

    private static string Name(MetadataReader reader, StringHandle typeNamespace, StringHandle typeName)
    {
        var space = reader.GetString(typeNamespace);
        var name = reader.GetString(typeName);
        return space.Length == 0 ? name : space + "." + name;
    }
}
