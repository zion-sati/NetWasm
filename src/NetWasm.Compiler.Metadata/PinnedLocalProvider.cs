using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

/// <summary>
/// Decodes each local of a local signature to whether the signature pins it, as
/// <c>fixed</c> pins the object whose address it takes.
/// </summary>
internal sealed class PinnedLocalProvider : ISignatureTypeProvider<bool, object?>
{
    public static PinnedLocalProvider Instance { get; } = new();

    public bool GetPinnedType(bool elementType) => true;

    public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;

    public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;

    public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;

    public bool GetTypeFromSpecification(
        MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => false;

    public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) => false;

    public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;

    public bool GetSZArrayType(bool elementType) => false;

    public bool GetArrayType(bool elementType, ArrayShape shape) => false;

    public bool GetByReferenceType(bool elementType) => false;

    public bool GetPointerType(bool elementType) => false;

    public bool GetFunctionPointerType(MethodSignature<bool> signature) => false;

    public bool GetGenericMethodParameter(object? genericContext, int index) => false;

    public bool GetGenericTypeParameter(object? genericContext, int index) => false;
}
