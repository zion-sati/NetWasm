using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public enum CliValueKind
{
    Void,
    I4,
    I8,
    F4,
    F8,
    NativeInt,
    ManagedReference,
    ValueType,
    ManagedAddress,
    Unknown,
}

public enum CliTypeShape
{
    Primitive,
    Named,
    SzArray,
    Array,
    GenericInstantiation,
    GenericTypeParameter,
    GenericMethodParameter,
    ManagedByReference,
    UnmanagedPointer,
}

/// <summary>
/// The structural CLI identity of a signature type. This is deliberately
/// separate from <see cref="CliValueKind"/>, which describes only the value's
/// current Wasm stack representation.
/// </summary>
public sealed class CliTypeIdentity : IEquatable<CliTypeIdentity>
{
    private CliTypeIdentity(
        string canonicalName,
        CliTypeShape shape,
        CliValueKind stackKind,
        bool isValueType,
        CliTypeIdentity? elementType = null,
        ImmutableArray<CliTypeIdentity> typeArguments = default,
        int genericParameterIndex = -1,
        int arrayRank = 0,
        AssemblyIdentity? assembly = null,
        string? fullName = null,
        CliTypeIdentity? stackStorageType = null)
    {
        CanonicalName = canonicalName;
        Shape = shape;
        StackKind = stackKind;
        IsValueType = isValueType;
        ElementType = elementType;
        TypeArguments = typeArguments.IsDefault
            ? []
            : typeArguments;
        GenericParameterIndex = genericParameterIndex;
        ArrayRank = arrayRank;
        Assembly = assembly;
        FullName = fullName;
        StackStorageType = stackStorageType;
    }

    public string CanonicalName { get; }
    public CliTypeShape Shape { get; }
    public CliValueKind StackKind { get; }

    public bool HasRuntimeStorage => StackKind != CliValueKind.Void;
    public bool IsValueType { get; }
    public CliTypeIdentity? ElementType { get; }
    public ImmutableArray<CliTypeIdentity> TypeArguments { get; }
    public int GenericParameterIndex { get; }
    public int ArrayRank { get; }
    public AssemblyIdentity? Assembly { get; }
    public string? FullName { get; }
    public CliTypeIdentity? StackStorageType { get; }

    public bool ContainsGenericParameters => Shape switch
    {
        CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter => true,
        CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer => ElementType!.ContainsGenericParameters,
        CliTypeShape.GenericInstantiation =>
            TypeArguments.Any(argument => argument.ContainsGenericParameters),
        _ => false,
    };

    public static CliTypeIdentity FromStackKind(CliValueKind kind) => kind switch
    {
        CliValueKind.Void => Primitive("void", CliValueKind.Void),
        CliValueKind.I4 => Primitive("i4", CliValueKind.I4),
        CliValueKind.I8 => Primitive("i8", CliValueKind.I8),
        CliValueKind.F4 => Primitive("f4", CliValueKind.F4),
        CliValueKind.F8 => Primitive("f8", CliValueKind.F8),
        CliValueKind.NativeInt => Primitive("nativeint", CliValueKind.NativeInt),
        CliValueKind.ManagedReference => Primitive(
            "object", CliValueKind.ManagedReference, isValueType: false),
        CliValueKind.ValueType => Named(
            new AssemblyIdentity("<unknown>"), "", "value", isValueType: true),
        CliValueKind.ManagedAddress => ManagedByReference(
            Named(new AssemblyIdentity("<unknown>"), "", "value", isValueType: true)),
        CliValueKind.Unknown => GenericParameter(method: false, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static CliTypeIdentity Primitive(
        string name,
        CliValueKind stackKind,
        bool isValueType = true) => new(
            $"primitive:{name}",
            CliTypeShape.Primitive,
            stackKind,
            isValueType);

    public static CliTypeIdentity Named(
        AssemblyIdentity assembly,
        string @namespace,
        string name,
        bool isValueType)
    {
        var fullName = string.IsNullOrEmpty(@namespace)
            ? name
            : $"{@namespace}.{name}";
        return fullName switch
        {
            "System.Void" => Primitive("void", CliValueKind.Void),
            "System.Boolean" => Primitive("bool", CliValueKind.I4),
            "System.Char" => Primitive("char", CliValueKind.I4),
            "System.SByte" => Primitive("i1", CliValueKind.I4),
            "System.Byte" => Primitive("u1", CliValueKind.I4),
            "System.Int16" => Primitive("i2", CliValueKind.I4),
            "System.UInt16" => Primitive("u2", CliValueKind.I4),
            "System.Int32" => Primitive("i4", CliValueKind.I4),
            "System.UInt32" => Primitive("u4", CliValueKind.I4),
            "System.Int64" => Primitive("i8", CliValueKind.I8),
            "System.UInt64" => Primitive("u8", CliValueKind.I8),
            "System.Single" => Primitive("f4", CliValueKind.F4),
            "System.Double" => Primitive("f8", CliValueKind.F8),
            "System.IntPtr" => Primitive("nativeint", CliValueKind.NativeInt),
            "System.UIntPtr" => Primitive("nativeuint", CliValueKind.NativeInt),
            "System.String" => Primitive(
                "string", CliValueKind.ManagedReference, isValueType: false),
            "System.Object" => Primitive(
                "object", CliValueKind.ManagedReference, isValueType: false),
            "System.RuntimeTypeHandle" => new(
                $"[{assembly.Name}]{fullName}",
                CliTypeShape.Named,
                CliValueKind.I4,
                isValueType,
                assembly: assembly,
                fullName: fullName),
            "System.StringComparison" => new(
                $"[{assembly.Name}]{fullName}",
                CliTypeShape.Named,
                CliValueKind.I4,
                isValueType,
                assembly: assembly,
                fullName: fullName),
            _ => new(
                $"[{assembly.Name}]{fullName}",
                CliTypeShape.Named,
                isValueType
                    ? CliValueKind.ValueType
                    : CliValueKind.ManagedReference,
                isValueType,
                assembly: assembly,
                fullName: fullName),
        };
    }

    public static CliTypeIdentity Named(
        AssemblyIdentity assembly,
        string @namespace,
        string name,
        bool isValueType,
        CliValueKind stackKind)
    {
        var identity = Named(assembly, @namespace, name, isValueType);
        var defaultStackKind = isValueType
            ? CliValueKind.ValueType
            : CliValueKind.ManagedReference;

        if (identity.Shape != CliTypeShape.Named || identity.StackKind != defaultStackKind)
        {
            if (identity.StackKind != stackKind)
            {
                throw new ArgumentException(
                    "The requested stack kind conflicts with the canonical CLI type identity.",
                    nameof(stackKind));
            }

            return identity;
        }

        if ((!isValueType && stackKind != CliValueKind.ManagedReference)
            || (isValueType && stackKind is CliValueKind.Void
                or CliValueKind.ManagedReference
                or CliValueKind.ManagedAddress
                or CliValueKind.Unknown))
        {
            throw new ArgumentException(
                "The requested stack kind is incompatible with the named CLI type.",
                nameof(stackKind));
        }

        return identity.WithStackKind(stackKind);
    }

    public static CliTypeIdentity FromDefinition(TypeDefinitionModel type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var hasExplicitStorage = type.IsEnum || type.EnumUnderlyingType.StackKind != CliValueKind.ValueType;
        if (!hasExplicitStorage)
        {
            return Named(type.Key.Assembly, type.Namespace, type.Name, type.IsValueType);
        }

        var identity = Named(
            type.Key.Assembly,
            type.Namespace,
            type.Name,
            type.IsValueType,
            type.EnumUnderlyingType.StackKind);

        return identity.WithStackStorageType(type.EnumUnderlyingType);
    }

    public static CliTypeIdentity SzArray(CliTypeIdentity elementType) => new(
        $"{elementType.CanonicalName}[]",
        CliTypeShape.SzArray,
        CliValueKind.ManagedReference,
        isValueType: false,
        elementType);

    public static CliTypeIdentity Array(CliTypeIdentity elementType, int rank) => new(
        $"{elementType.CanonicalName}[{(rank == 1 ? "*" : new string(',', rank - 1))}]",
        CliTypeShape.Array,
        CliValueKind.ManagedReference,
        isValueType: false,
        elementType,
        arrayRank: rank);

    public static CliTypeIdentity GenericInstantiation(
        CliTypeIdentity genericType,
        ImmutableArray<CliTypeIdentity> typeArguments) => new(
            $"{genericType.CanonicalName}<{string.Join(',', typeArguments.Select(type => type.CanonicalName))}>",
            CliTypeShape.GenericInstantiation,
            genericType.StackKind,
            genericType.IsValueType,
            elementType: genericType,
            typeArguments: typeArguments);

    public static CliTypeIdentity GenericParameter(bool method, int index) => new(
        $"{(method ? "!!" : "!")}{index}",
        method ? CliTypeShape.GenericMethodParameter : CliTypeShape.GenericTypeParameter,
        CliValueKind.Unknown,
        isValueType: false,
        genericParameterIndex: index);

    public static CliTypeIdentity ManagedByReference(CliTypeIdentity elementType) => new(
        $"{elementType.CanonicalName}&",
        CliTypeShape.ManagedByReference,
        CliValueKind.ManagedAddress,
        isValueType: true,
        elementType);

    public static CliTypeIdentity UnmanagedPointer(CliTypeIdentity elementType) => new(
        $"{elementType.CanonicalName}*",
        CliTypeShape.UnmanagedPointer,
        CliValueKind.NativeInt,
        isValueType: true,
        elementType);

    public CliTypeIdentity WithStackKind(CliValueKind stackKind) => new(
        CanonicalName,
        Shape,
        stackKind,
        IsValueType,
        ElementType,
        TypeArguments,
        GenericParameterIndex,
        ArrayRank,
        Assembly,
        FullName,
        StackStorageType);

    public CliTypeIdentity WithStackStorageType(CliTypeIdentity storageType)
    {
        ArgumentNullException.ThrowIfNull(storageType);
        return new(
            CanonicalName,
            Shape,
            storageType.StackKind,
            IsValueType,
            ElementType,
            TypeArguments,
            GenericParameterIndex,
            ArrayRank,
            Assembly,
            FullName,
            storageType);
    }

    public CliTypeIdentity Substitute(
        ImmutableArray<CliTypeIdentity> typeArguments,
        ImmutableArray<CliTypeIdentity> methodArguments = default)
    {
        typeArguments = typeArguments.IsDefault ? [] : typeArguments;
        methodArguments = methodArguments.IsDefault ? [] : methodArguments;
        return Shape switch
        {
            CliTypeShape.GenericTypeParameter when
                (uint)GenericParameterIndex < (uint)typeArguments.Length =>
                typeArguments[GenericParameterIndex],
            CliTypeShape.GenericMethodParameter when
                (uint)GenericParameterIndex < (uint)methodArguments.Length =>
                methodArguments[GenericParameterIndex],
            CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter =>
                this,
            CliTypeShape.SzArray => SzArray(
                ElementType!.Substitute(typeArguments, methodArguments)),
            CliTypeShape.Array => Array(
                ElementType!.Substitute(typeArguments, methodArguments), ArrayRank),
            CliTypeShape.ManagedByReference => ManagedByReference(
                ElementType!.Substitute(typeArguments, methodArguments)),
            CliTypeShape.UnmanagedPointer => UnmanagedPointer(
                ElementType!.Substitute(typeArguments, methodArguments)),
            CliTypeShape.GenericInstantiation => GenericInstantiation(
                ElementType!.Substitute(typeArguments, methodArguments),
                [.. TypeArguments.Select(argument =>
                    argument.Substitute(typeArguments, methodArguments))]),
            _ => this,
        };
    }

    public bool Equals(CliTypeIdentity? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(CanonicalName, other.CanonicalName);

    public override bool Equals(object? obj) => Equals(obj as CliTypeIdentity);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(CanonicalName);

    public override string ToString() => CanonicalName;
}

public readonly record struct CliGenericContext(
    ImmutableArray<CliTypeIdentity> TypeArguments,
    ImmutableArray<CliTypeIdentity> MethodArguments)
{
    public static CliGenericContext Empty { get; } = new([], []);

    public CliGenericContext Normalize() => new(
        TypeArguments.IsDefault ? [] : TypeArguments,
        MethodArguments.IsDefault ? [] : MethodArguments);
}

public readonly record struct AssemblyIdentity(string Name)
{
    public override string ToString() => Name;
}

public readonly record struct EntityKey(AssemblyIdentity Assembly, int MetadataToken)
{
    public override string ToString() => $"{Assembly}:0x{MetadataToken:x8}";
}

public enum CliTypeLayoutKind
{
    Auto,
    Sequential,
    Explicit,
}

public enum CliGenericVariance
{
    Invariant,
    Covariant,
    Contravariant,
}

public sealed record TypeDefinitionModel(
    EntityKey Key,
    string Namespace,
    string Name,
    bool IsValueType,
    ImmutableArray<EntityKey> Fields,
    ImmutableArray<EntityKey> Methods)
{
    public CliTypeLayoutKind LayoutKind { get; init; }
    public int PackingSize { get; init; }
    public int DeclaredSize { get; init; }
    public int InlineArrayLength { get; init; }
    public int GenericArity { get; init; }
    public ImmutableArray<CliGenericVariance> GenericParameterVariances { get; init; } = [];
    public bool IsInterface { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public bool IsBeforeFieldInit { get; init; }
    public bool IsEnum { get; init; }
    public CliTypeIdentity EnumUnderlyingType { get; init; } =
        CliTypeIdentity.FromStackKind(CliValueKind.ValueType);
    public CliValueKind EnumUnderlyingKind => EnumUnderlyingType.StackKind;
    public ImmutableArray<EnumMemberModel> EnumMembers { get; init; } = [];
    public bool IsFlagsEnum { get; init; }

    public string FullName => string.IsNullOrEmpty(Namespace)
        ? Name
        : $"{Namespace}.{Name}";
}

public sealed record FieldDefinitionModel(
    EntityKey Key,
    EntityKey DeclaringType,
    string Name,
    CliValueKind FieldType,
    bool IsStatic)
{
    public ImmutableArray<byte> InitialData { get; init; } = [];
    public ulong? LiteralValue { get; init; }
    public int? ExplicitOffset { get; init; }
    public CliTypeIdentity SignatureType { get; init; } =
        CliTypeIdentity.FromStackKind(FieldType);

    public FieldDefinitionModel(
        EntityKey key,
        EntityKey declaringType,
        string name,
        CliTypeIdentity signatureType,
        bool isStatic)
        : this(key, declaringType, name, signatureType.StackKind, isStatic)
    {
        SignatureType = signatureType;
    }

    public string DisplayName(string declaringTypeName) =>
        $"{declaringTypeName}::{Name}";
}

public sealed record MethodSignatureModel(
    CliValueKind ReturnType,
    ImmutableArray<CliValueKind> ParameterTypes)
{
    public CliTypeIdentity ReturnSignatureType { get; init; } =
        CliTypeIdentity.FromStackKind(ReturnType);

    public ImmutableArray<CliTypeIdentity> ParameterSignatureTypes { get; init; } =
        [.. ParameterTypes.Select(CliTypeIdentity.FromStackKind)];

    public MethodSignatureModel(
        CliTypeIdentity returnType,
        ImmutableArray<CliTypeIdentity> parameterTypes)
        : this(returnType.StackKind, [.. parameterTypes.Select(type => type.StackKind)])
    {
        ReturnSignatureType = returnType;
        ParameterSignatureTypes = parameterTypes;
    }

    public static MethodSignatureModel Create(
        CliValueKind returnType,
        params CliValueKind[] parameterTypes) =>
        new(returnType, [.. parameterTypes]);

    public static MethodSignatureModel Create(
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameterTypes) =>
        new(returnType, [.. parameterTypes]);

    public MethodSignatureModel Substitute(
        ImmutableArray<CliTypeIdentity> typeArguments,
        ImmutableArray<CliTypeIdentity> methodArguments = default) => new(
            ReturnSignatureType.Substitute(typeArguments, methodArguments),
            [.. ParameterSignatureTypes.Select(type =>
                type.Substitute(typeArguments, methodArguments))]);
}

public sealed record MethodDefinitionModel(
    EntityKey Key,
    EntityKey DeclaringType,
    string Name,
    bool IsStatic,
    MethodSignatureModel Signature,
    int RelativeVirtualAddress)
{
    public int GenericArity { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsNewSlot { get; init; }
    public bool IsFinal { get; init; }
    public bool IsAbstract { get; init; }
    public InteropImportDeclaration? JSImport { get; init; }
    public InteropExportDeclaration? JSExport { get; init; }
    public WitImportDeclaration? WitImport { get; init; }
    public WitExportDeclaration? WitExport { get; init; }
    public WitPostReturnDeclaration? WitPostReturn { get; init; }

    public bool HasBody => RelativeVirtualAddress != 0;

    public ImmutableArray<CliValueKind> WasmParameterTypes => IsStatic
        ? Signature.ParameterTypes
        : Signature.ParameterTypes.Insert(0, CliValueKind.ManagedReference);
}

public sealed record MethodInstanceModel(
    MethodDefinitionModel Definition,
    CliTypeIdentity DeclaringType,
    ImmutableArray<CliTypeIdentity> MethodArguments,
    MethodSignatureModel Signature)
{
    public string CanonicalName =>
        $"{DeclaringType.CanonicalName}::0x{Definition.Key.MetadataToken:x8}" +
        (MethodArguments.IsEmpty
            ? ""
            : $"<{string.Join(',', MethodArguments.Select(type => type.CanonicalName))}>");

    public bool IsConstructed =>
        DeclaringType.Shape is CliTypeShape.GenericInstantiation or CliTypeShape.Array ||
        !MethodArguments.IsEmpty;
}

public sealed record FieldInstanceModel(
    FieldDefinitionModel Definition,
    CliTypeIdentity DeclaringType,
    CliTypeIdentity FieldType)
{
    public string CanonicalName =>
        $"{DeclaringType.CanonicalName}::0x{Definition.Key.MetadataToken:x8}";

    public bool IsConstructed =>
        DeclaringType.Shape == CliTypeShape.GenericInstantiation;
}

public sealed record MethodImplementationModel(
    MethodDefinitionModel Body,
    MethodDefinitionModel Declaration);

public sealed record MethodImplementationInstanceModel(
    MethodInstanceModel Body,
    MethodInstanceModel Declaration);

public sealed record DispatchTargetModel(
    CliTypeIdentity ReceiverType,
    MethodInstanceModel Method);

public sealed record DispatchCallSiteModel(
    string Caller,
    int IlOffset,
    MethodInstanceModel Declaration,
    ImmutableArray<DispatchTargetModel> Targets)
{
    public string Key => $"{Caller}@{IlOffset:x8}";
}

public sealed record TypeTestSiteModel(
    string Caller,
    int IlOffset,
    CliTypeIdentity TargetType,
    ImmutableArray<CliTypeIdentity> MatchingTypes)
{
    public string Key => $"{Caller}@{IlOffset:x8}";
}

public interface ITypeRepository
{
    TypeDefinitionModel GetTypeDefinition(EntityKey key);
}

public interface IFieldRepository
{
    FieldDefinitionModel GetField(EntityKey key);
}

public interface IMethodRepository
{
    MethodDefinitionModel GetMethod(EntityKey key);
}

public interface ISymbolFormatter
{
    string Format(EntityKey key);
    string Format(MethodDefinitionModel method);
}

public interface ITypeClassifier
{
    bool IsDelegateType(EntityKey type);
}

public enum RuntimeIntrinsic
{
    StringLength,
    ArrayLength,
    ArrayRank,
    ArrayGetLength,
    ArrayGetLowerBound,
    ArrayGetValue,
    ArrayCopy,
    ArrayClear,
    ArrayClone,
    StringCharacterAt,
    StringSetCharacterUnchecked,
    GcCollect,
    WeakHandleCreate,
    WeakHandleGet,
    WeakHandleSet,
    WeakHandleRelease,
    GcHandleCreate,
    GcHandleGet,
    GcHandleAddress,
    GcHandleSet,
    GcHandleRelease,
    GcGetMetric,
    GcMetricIsSupported,
    GcWaitForPendingFinalizers,
    ObjectIdentityHash,
    ValueTypeEquals,
    ValueTypeGetHashCode,
    SuppressFinalize,
    ReRegisterForFinalize,
    ReportUnobservedTaskException,
    JSObjectDispose,
    JSSubscriptionDispose,
    IsReferenceOrContainsReferences,
    UnsafeAdd,
    UnsafeAs,
    UnsafeAsRef,
    UnsafeAsRefManaged,
    UnsafeNullRef,
    UnsafeIsAddressGreaterThan,
    UnsafeSizeOf,
    UnsafeByteOffset,
    UnsafeAddByteOffset,
    UnsafeObjectAs,
    UnsafeUnbox,
    NativeMemoryAlloc,
    NativeMemoryRealloc,
    NativeMemoryFree,
    NativeMemoryAlignedAlloc,
    NativeMemoryAlignedRealloc,
    NativeMemoryAlignedFree,
    GetArrayDataReference,
    NullableGetUnderlyingType,
    EnumEquals,
    EnumGetHashCode,
    EnumCompareTo,
    EnumGetTypeCode,
    EnumHasFlag,
    EnumGetNames,
    EnumGetName,
    EnumGetValues,
    EnumIsDefined,
    EnumParse,
    EnumGetUnderlyingType,
    EnumToString,
    EnumFormat,
    EnumToObject,
    EnumConvert,
    NativeIntegerSize,
    SingleToInt32Bits,
    Int32BitsToSingle,
    DoubleToInt64Bits,
    Int64BitsToDouble,
    FloatingAbsolute,
    FloatingCeiling,
    FloatingFloor,
    FloatingTruncate,
    FloatingRound,
    FloatingSquareRoot,
    ComponentReallocate,
    ComponentFree,
    ComponentResourceHandleCreate,
    ComponentResourceHandleGet,
    ComponentResourceHandleRelease,
}

public interface IRuntimeIntrinsicRegistry
{
    bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic);
}
