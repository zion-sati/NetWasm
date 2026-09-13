using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class SignatureTypeProvider : ISignatureTypeProvider<CliTypeIdentity, object?>
{
    private readonly AssemblyIdentity _sourceAssembly;
    private readonly MetadataReader? _owningReader;
    private readonly AssemblyIdentityAliases _assemblyIdentityAliases;

    internal SignatureTypeProvider(
        AssemblyIdentity sourceAssembly,
        MetadataReader? owningReader = null,
        AssemblyIdentityAliases? assemblyIdentityAliases = null)
    {
        _assemblyIdentityAliases = assemblyIdentityAliases ?? AssemblyIdentityAliases.Empty;
        _sourceAssembly = _assemblyIdentityAliases.Canonicalize(sourceAssembly);
        _owningReader = owningReader;
    }
    public CliTypeIdentity GetArrayType(CliTypeIdentity elementType, ArrayShape shape) =>
        CliTypeIdentity.Array(elementType, shape.Rank);

    public CliTypeIdentity GetByReferenceType(CliTypeIdentity elementType) =>
        CliTypeIdentity.ManagedByReference(elementType);

    public CliTypeIdentity GetFunctionPointerType(MethodSignature<CliTypeIdentity> signature) =>
        CliTypeIdentity.UnmanagedPointer(
            CliTypeIdentity.Primitive("void", CliValueKind.Void));

    public CliTypeIdentity GetGenericInstantiation(
        CliTypeIdentity genericType,
        ImmutableArray<CliTypeIdentity> typeArguments) =>
        CliTypeIdentity.GenericInstantiation(genericType, typeArguments);

    public CliTypeIdentity GetGenericMethodParameter(object? genericContext, int index) =>
        TryGetArgument(genericContext, method: true, index) ??
        CliTypeIdentity.GenericParameter(method: true, index);

    public CliTypeIdentity GetGenericTypeParameter(object? genericContext, int index) =>
        TryGetArgument(genericContext, method: false, index) ??
        CliTypeIdentity.GenericParameter(method: false, index);

    public CliTypeIdentity GetModifiedType(
        CliTypeIdentity modifier,
        CliTypeIdentity unmodifiedType,
        bool isRequired) => unmodifiedType;

    public CliTypeIdentity GetPinnedType(CliTypeIdentity elementType) => elementType;

    public CliTypeIdentity GetPointerType(CliTypeIdentity elementType) =>
        CliTypeIdentity.UnmanagedPointer(elementType);

    public CliTypeIdentity GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => Primitive("void", CliValueKind.Void),
        PrimitiveTypeCode.Boolean => Primitive("bool"),
        PrimitiveTypeCode.Char => Primitive("char"),
        PrimitiveTypeCode.SByte => Primitive("i1"),
        PrimitiveTypeCode.Byte => Primitive("u1"),
        PrimitiveTypeCode.Int16 => Primitive("i2"),
        PrimitiveTypeCode.UInt16 => Primitive("u2"),
        PrimitiveTypeCode.Int32 => Primitive("i4"),
        PrimitiveTypeCode.UInt32 => Primitive("u4"),
        PrimitiveTypeCode.Int64 => Primitive("i8", CliValueKind.I8),
        PrimitiveTypeCode.UInt64 => Primitive("u8", CliValueKind.I8),
        PrimitiveTypeCode.Single => Primitive("f4", CliValueKind.F4),
        PrimitiveTypeCode.Double => Primitive("f8", CliValueKind.F8),
        PrimitiveTypeCode.IntPtr => Primitive("nativeint", CliValueKind.NativeInt),
        PrimitiveTypeCode.UIntPtr => Primitive("nativeuint", CliValueKind.NativeInt),
        PrimitiveTypeCode.String => PrimitiveReference("string"),
        PrimitiveTypeCode.Object => PrimitiveReference("object"),
        PrimitiveTypeCode.TypedReference => Primitive(
            "typedref", CliValueKind.ValueType),
        _ => throw Unsupported($"primitive signature '{typeCode}'"),
    };

    public CliTypeIdentity GetSZArrayType(CliTypeIdentity elementType) =>
        CliTypeIdentity.SzArray(elementType);

    public CliTypeIdentity GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        var typeNamespace = reader.GetString(definition.Namespace);
        var typeName = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            typeNamespace = "";
            typeName = GetDefinitionFullName(reader, declaringType) + "+" + typeName;
        }
        var identity = CliTypeIdentity.Named(
            _sourceAssembly,
            typeNamespace,
            typeName,
            IsValueType(rawTypeKind));
        if (identity.Shape == CliTypeShape.Named &&
            IsEnumDefinition(reader, handle))
        {
            var storageType = definition.GetFields()
                .Select(reader.GetFieldDefinition)
                .Where(field => reader.GetString(field.Name) == "value__")
                .Select(field => field.DecodeSignature(this, genericContext: null))
                .Single();
            identity = identity.WithStackStorageType(storageType);
        }
        return identity;
    }

    public CliTypeIdentity GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        (var assembly, var fullName) = GetReferenceIdentity(reader, handle);
        return CliTypeIdentity.Named(
            assembly,
            "",
            fullName,
            IsValueType(rawTypeKind));
    }

    public CliTypeIdentity GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static CliTypeIdentity Primitive(
        string name,
        CliValueKind stackKind = CliValueKind.I4) =>
        CliTypeIdentity.Primitive(name, stackKind);

    private static CliTypeIdentity PrimitiveReference(string name) =>
        CliTypeIdentity.Primitive(name, CliValueKind.ManagedReference, isValueType: false);

    private (AssemblyIdentity Assembly, string FullName) GetReferenceIdentity(
        MetadataReader reader,
        TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var scope = reference.ResolutionScope;
        var typeNamespace = reader.GetString(reference.Namespace);
        var typeName = reader.GetString(reference.Name);
        if (scope.Kind == HandleKind.AssemblyReference)
        {
            var assemblyReference = reader.GetAssemblyReference(
                (AssemblyReferenceHandle)scope);
            return (
                _assemblyIdentityAliases.Canonicalize(new AssemblyIdentity(
                    reader.GetString(assemblyReference.Name))),
                Qualify(typeNamespace, typeName));
        }

        if (scope.Kind == HandleKind.TypeReference)
        {
            (var assembly, var declaringName) = GetReferenceIdentity(
                reader, (TypeReferenceHandle)scope);
            return (assembly, declaringName + "+" + typeName);
        }

        return (_sourceAssembly, Qualify(typeNamespace, typeName));
    }

    private static string GetDefinitionFullName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        return declaringType.IsNil
            ? Qualify(reader.GetString(definition.Namespace), name)
            : GetDefinitionFullName(reader, declaringType) + "+" + name;
    }

    private static string Qualify(string typeNamespace, string typeName) =>
        string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";

    private static bool IsValueType(byte rawTypeKind) => rawTypeKind switch
    {
        0x11 => true,
        0x00 or 0x12 => false,
        _ => throw Unsupported($"named signature kind 0x{rawTypeKind:x2}"),
    };

    private bool IsEnumDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        reader = _owningReader ?? reader;
        var definition = reader.GetTypeDefinition(handle);
        var baseType = definition.BaseType;
        (var typeNamespace, var typeName) = baseType.Kind switch
        {
            HandleKind.TypeReference => ReferenceName(
                reader, (TypeReferenceHandle)baseType),
            HandleKind.TypeDefinition => IsSystemEnumDefinition(reader, baseType)
                ? ("System", "Enum")
                : (string.Empty, string.Empty),
            _ => (string.Empty, string.Empty),
        };
        return typeNamespace == "System" && typeName == "Enum";

        static (string Namespace, string Name) ReferenceName(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            var reference = reader.GetTypeReference(handle);
            return (reader.GetString(reference.Namespace), reader.GetString(reference.Name));
        }

        static bool IsSystemEnumDefinition(
            MetadataReader reader,
            EntityHandle baseType)
        {
            foreach (var candidate in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(candidate);
                if (reader.GetString(definition.Namespace) == "System" &&
                    reader.GetString(definition.Name) == "Enum")
                {
                    return (EntityHandle)candidate == baseType;
                }
            }
            return false;
        }

    }

    private static CliTypeIdentity? TryGetArgument(
        object? genericContext,
        bool method,
        int index)
    {
        if (genericContext is not CliGenericContext context)
        {
            return null;
        }
        context = context.Normalize();
        var arguments = method
            ? context.MethodArguments
            : context.TypeArguments;
        return (uint)index < (uint)arguments.Length ? arguments[index] : null;
    }

    private static CompilerException Unsupported(string shape) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"unsupported {shape}"));
}
