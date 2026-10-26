using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

/// <summary>
/// Renders the type of a [JSMarshalAs&lt;T&gt;] attribute as text, with Mono's JSType
/// markers by their own names: <c>Promise&lt;Number&gt;</c> for
/// <c>JSType.Promise&lt;JSType.Number&gt;</c>. Any other type keeps its full metadata
/// name, which names no marker.
/// </summary>
internal sealed class JSTypeMarkerProvider(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    private const string JSType = "System.Runtime.InteropServices.JavaScript.JSType";

    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var declaring = GetTypeFromReference(metadata, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind);
            return declaring == JSType ? Marker(name) : declaring + "+" + name;
        }
        var @namespace = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            var declaring = GetTypeFromDefinition(metadata, declaringType, rawTypeKind);
            return declaring == JSType ? Marker(name) : declaring + "+" + name;
        }
        var @namespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        $"{genericType}<{string.Join(",", typeArguments)}>";

    public string GetTypeFromSpecification(
        MetadataReader metadata, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[*]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetFunctionPointerType(MethodSignature<string> signature) => "method";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

    // A generic marker drops its arity: Promise`1 is Promise.
    private static string Marker(string name) => name.IndexOf('`') is var tick and >= 0 ? name[..tick] : name;
}
