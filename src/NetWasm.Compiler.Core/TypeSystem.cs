using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NetWasm.Compiler.Core.NativeInterop;

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
    // Vector128<T> and Vector<T>: 16 bytes in a Wasm v128 value.
    V128,
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
    FunctionPointer,
    Modified,
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
        CliTypeIdentity? stackStorageType = null,
        CliFunctionPointerSignature? functionPointerSignature = null,
        CliTypeIdentity? customModifier = null,
        bool isRequiredModifier = false)
    {
        CanonicalName = canonicalName;
        // Identities key the compiler's hottest dictionaries; hashing the canonical name, which
        // for a generic instantiation is long, on every lookup cost more than the lookup.
        _hashCode = StringComparer.Ordinal.GetHashCode(canonicalName);
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
        FunctionPointerSignature = functionPointerSignature;
        CustomModifier = customModifier;
        IsRequiredModifier = isRequiredModifier;
    }

    private readonly int _hashCode;

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
    public CliFunctionPointerSignature? FunctionPointerSignature { get; }
    public CliTypeIdentity? CustomModifier { get; }
    public bool IsRequiredModifier { get; }

    public bool ContainsGenericParameters => Shape switch
    {
        CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter => true,
        CliTypeShape.SzArray or CliTypeShape.Array or CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer or CliTypeShape.Modified => ElementType!.ContainsGenericParameters,
        CliTypeShape.FunctionPointer =>
            FunctionPointerSignature!.Signature.ReturnSignatureType.ContainsGenericParameters ||
            FunctionPointerSignature.Signature.ParameterSignatureTypes.Any(type => type.ContainsGenericParameters),
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
        CliValueKind.V128 => new(
            "[<unknown>]v128", CliTypeShape.Named, CliValueKind.V128, isValueType: true,
            assembly: new AssemblyIdentity("<unknown>"), fullName: "v128"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // Primitives are a handful of identities the compiler asks for constantly, so each is made
    // once and shared. A new one replaces the map by compare-and-swap.
    private static ImmutableDictionary<(string Name, CliValueKind StackKind, bool IsValueType), CliTypeIdentity>
        s_primitives = ImmutableDictionary<(string, CliValueKind, bool), CliTypeIdentity>.Empty;

    public static CliTypeIdentity Primitive(
        string name,
        CliValueKind stackKind,
        bool isValueType = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Volatile.Read(ref s_primitives).TryGetValue((name, stackKind, isValueType), out var shared)
            ? shared
            : ImmutableInterlocked.GetOrAdd(
                ref s_primitives,
                (name, stackKind, isValueType),
                static key => new CliTypeIdentity(
                    $"primitive:{key.Item1}",
                    CliTypeShape.Primitive,
                    key.Item2,
                    key.Item3));
    }

    // Identities are immutable and every factory is a function of its inputs, so equal inputs
    // share one identity. Named types are keyed by value; derived shapes by the identities
    // they are made from, so a cached identity is exactly the one the factory would build.
    // Each compile makes the same identities millions of times; sharing them removes most of
    // the allocation and the canonical names built for them.
    private static readonly ConcurrentDictionary<(string Assembly, string Namespace, string Name, bool IsValueType), CliTypeIdentity>
        s_named = new();
    private static readonly ConcurrentDictionary<InstantiationKey, CliTypeIdentity> s_instantiations = new();
    private static readonly ConditionalWeakTable<CliTypeIdentity, CliTypeIdentity> s_byReferences = new();
    private static readonly ConditionalWeakTable<CliTypeIdentity, CliTypeIdentity> s_szArrays = new();

    public static CliTypeIdentity Named(
        AssemblyIdentity assembly,
        string @namespace,
        string name,
        bool isValueType)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = (assembly.Name ?? "", @namespace ?? "", name, isValueType);
        return s_named.TryGetValue(key, out var shared)
            ? shared
            : s_named.GetOrAdd(key, static key => CreateNamed(
                new AssemblyIdentity(key.Assembly), key.Namespace, key.Name, key.IsValueType));
    }

    private static CliTypeIdentity CreateNamed(
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
            "System.RuntimeMethodHandle" or
            "System.RuntimeFieldHandle" => new(
                $"[{assembly.Name}]{fullName}",
                CliTypeShape.Named,
                CliValueKind.NativeInt,
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
            // Vector128<T> and Vector<T> are 16 bytes that move as one Wasm v128
            // value; their instantiations take this kind from the definition.
            // So do System.Numerics' float vectors, as RyuJIT's SIMD types:
            // Vector4, Quaternion and Plane fill the 16 bytes, Vector3 the low
            // 12 and Vector2 the low 8, the lanes above them undefined.
            "System.Runtime.Intrinsics.Vector128`1" or
            "System.Numerics.Vector`1" or
            "System.Numerics.Vector2" or
            "System.Numerics.Vector3" or
            "System.Numerics.Vector4" or
            "System.Numerics.Quaternion" or
            "System.Numerics.Plane" => new(
                $"[{assembly.Name}]{fullName}",
                CliTypeShape.Named,
                CliValueKind.V128,
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

    public static CliTypeIdentity SzArray(CliTypeIdentity elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        return s_szArrays.TryGetValue(elementType, out var shared)
            ? shared
            : s_szArrays.GetValue(elementType, static element => new(
                $"{element.CanonicalName}[]",
                CliTypeShape.SzArray,
                CliValueKind.ManagedReference,
                isValueType: false,
                element));
    }

    public static CliTypeIdentity Array(CliTypeIdentity elementType, int rank) => new(
        $"{elementType.CanonicalName}[{(rank == 1 ? "*" : new string(',', rank - 1))}]",
        CliTypeShape.Array,
        CliValueKind.ManagedReference,
        isValueType: false,
        elementType,
        arrayRank: rank);

    public static CliTypeIdentity GenericInstantiation(
        CliTypeIdentity genericType,
        ImmutableArray<CliTypeIdentity> typeArguments)
    {
        ArgumentNullException.ThrowIfNull(genericType);
        typeArguments = typeArguments.IsDefault ? [] : typeArguments;
        var key = new InstantiationKey(genericType, typeArguments);
        return s_instantiations.TryGetValue(key, out var shared)
            ? shared
            : s_instantiations.GetOrAdd(key, static key => new(
                $"{key.GenericType.CanonicalName}<{string.Join(',', key.TypeArguments.Select(type => type.CanonicalName))}>",
                CliTypeShape.GenericInstantiation,
                key.GenericType.StackKind,
                key.GenericType.IsValueType,
                elementType: key.GenericType,
                typeArguments: key.TypeArguments,
                stackStorageType: key.GenericType.StackStorageType));
    }

    // An instantiation by the identities it is made from (by reference): a variant of an
    // argument, such as one with another stack kind, is another key.
    private readonly record struct InstantiationKey(
        CliTypeIdentity GenericType,
        ImmutableArray<CliTypeIdentity> TypeArguments)
    {
        public bool Equals(InstantiationKey other)
        {
            if (!ReferenceEquals(GenericType, other.GenericType) ||
                TypeArguments.Length != other.TypeArguments.Length)
            {
                return false;
            }
            for (var index = 0; index < TypeArguments.Length; index++)
            {
                if (!ReferenceEquals(TypeArguments[index], other.TypeArguments[index])) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(GenericType));
            foreach (var argument in TypeArguments) hash.Add(RuntimeHelpers.GetHashCode(argument));
            return hash.ToHashCode();
        }
    }

    public static CliTypeIdentity GenericParameter(bool method, int index) => new(
        $"{(method ? "!!" : "!")}{index}",
        method ? CliTypeShape.GenericMethodParameter : CliTypeShape.GenericTypeParameter,
        CliValueKind.Unknown,
        isValueType: false,
        genericParameterIndex: index);

    public static CliTypeIdentity ScopedGenericParameter(
        string ownerCanonicalName,
        bool method,
        int index)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerCanonicalName);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new(
            $"generic-parameter:{ownerCanonicalName}:{(method ? "!!" : "!")}{index}",
            method ? CliTypeShape.GenericMethodParameter : CliTypeShape.GenericTypeParameter,
            CliValueKind.Unknown,
            isValueType: false,
            genericParameterIndex: index);
    }

    public static CliTypeIdentity ManagedByReference(CliTypeIdentity elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        return s_byReferences.TryGetValue(elementType, out var shared)
            ? shared
            : s_byReferences.GetValue(elementType, static element => new(
                $"{element.CanonicalName}&",
                CliTypeShape.ManagedByReference,
                CliValueKind.ManagedAddress,
                isValueType: true,
                element));
    }

    public static CliTypeIdentity UnmanagedPointer(CliTypeIdentity elementType) => new(
        $"{elementType.CanonicalName}*",
        CliTypeShape.UnmanagedPointer,
        CliValueKind.NativeInt,
        isValueType: true,
        elementType);

    public static CliTypeIdentity FunctionPointer(CliFunctionPointerSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(signature.Signature);
        ArgumentOutOfRangeException.ThrowIfNegative(signature.GenericArity);
        ArgumentOutOfRangeException.ThrowIfNegative(signature.RequiredParameterCount);
        if (signature.RequiredParameterCount > signature.Signature.ParameterSignatureTypes.Length)
            throw new ArgumentOutOfRangeException(nameof(signature));
        return new(
            $"fnptr:{signature.Header:x2}:{signature.GenericArity}:{signature.RequiredParameterCount}:" +
            $"{signature.Signature.ReturnSignatureType.CanonicalName}" +
            $"({string.Join(',', signature.Signature.ParameterSignatureTypes.Select(type => type.CanonicalName))})",
            CliTypeShape.FunctionPointer,
            CliValueKind.NativeInt,
            isValueType: true,
            functionPointerSignature: signature);
    }

    public static CliTypeIdentity Modified(
        CliTypeIdentity elementType,
        CliTypeIdentity modifier,
        bool isRequired)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentNullException.ThrowIfNull(modifier);
        return new(
            $"{(isRequired ? "modreq" : "modopt")}({modifier.CanonicalName}){elementType.CanonicalName}",
            CliTypeShape.Modified,
            elementType.StackKind,
            elementType.IsValueType,
            elementType,
            customModifier: modifier,
            isRequiredModifier: isRequired);
    }

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
        StackStorageType,
        FunctionPointerSignature,
        CustomModifier,
        IsRequiredModifier);

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
            storageType,
            FunctionPointerSignature,
            CustomModifier,
            IsRequiredModifier);
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
            CliTypeShape.Modified => Modified(
                ElementType!.Substitute(typeArguments, methodArguments),
                CustomModifier!, IsRequiredModifier),
            CliTypeShape.FunctionPointer => FunctionPointer(FunctionPointerSignature! with
            {
                Signature = FunctionPointerSignature.Signature.Substitute(typeArguments, methodArguments),
            }),
            CliTypeShape.GenericInstantiation => GenericInstantiation(
                ElementType!.Substitute(typeArguments, methodArguments),
                SubstituteArguments(TypeArguments, typeArguments, methodArguments)),
            _ => this,
        };
    }

    // The arguments substituted; the same array when none changes, so the instantiation
    // is found again without allocating.
    private static ImmutableArray<CliTypeIdentity> SubstituteArguments(
        ImmutableArray<CliTypeIdentity> arguments,
        ImmutableArray<CliTypeIdentity> typeArguments,
        ImmutableArray<CliTypeIdentity> methodArguments)
    {
        CliTypeIdentity[]? changed = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var substituted = arguments[index].Substitute(typeArguments, methodArguments);
            if (changed is null && ReferenceEquals(substituted, arguments[index])) continue;
            if (changed is null)
            {
                changed = new CliTypeIdentity[arguments.Length];
                for (var copied = 0; copied < index; copied++) changed[copied] = arguments[copied];
            }
            changed[index] = substituted;
        }
        return changed is null ? arguments : ImmutableCollectionsMarshal.AsImmutableArray(changed);
    }

    public bool Equals(CliTypeIdentity? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        _hashCode == other._hashCode &&
        StringComparer.Ordinal.Equals(CanonicalName, other.CanonicalName);

    public override bool Equals(object? obj) => Equals(obj as CliTypeIdentity);

    public override int GetHashCode() => _hashCode;

    public override string ToString() => CanonicalName;
}

public sealed record CliFunctionPointerSignature(
    byte Header,
    int GenericArity,
    int RequiredParameterCount,
    MethodSignatureModel Signature);

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

    // Keys index the compiler's largest maps. The token separates entities within an
    // assembly; the assembly name's length and ends separate assemblies well enough without
    // hashing its text on every lookup (equality still compares it).
    public override int GetHashCode()
    {
        var name = Assembly.Name;
        return name is null || name.Length == 0
            ? MetadataToken
            : HashCode.Combine(MetadataToken, name.Length, name[0], name[^1]);
    }
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
    public ImmutableArray<string> GenericParameterNames { get; init; } = [];
    public ImmutableArray<CliGenericVariance> GenericParameterVariances { get; init; } = [];
    public bool IsInterface { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public bool IsBeforeFieldInit { get; init; }
    public bool IsEnum { get; init; }
    public CliTypeIdentity EnumUnderlyingType { get; init; } =
        CliTypeIdentity.FromStackKind(CliValueKind.ValueType);
    public ImmutableArray<EnumMemberModel> EnumMembers { get; init; } = [];
    public bool IsFlagsEnum { get; init; }

    private readonly DerivedString<(string, string)> _fullName = new();

    public string FullName => _fullName.Get(
        (Namespace, Name),
        static parts => string.IsNullOrEmpty(parts.Item1)
            ? parts.Item2
            : $"{parts.Item1}.{parts.Item2}");
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
    public bool IsInitOnly { get; init; }
    public bool IsLiteral { get; init; }
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
}

public sealed record MethodSignatureModel
{
    // A signature known only by its stack kinds; its types are those kinds' identities.
    public MethodSignatureModel(
        CliValueKind ReturnType,
        ImmutableArray<CliValueKind> ParameterTypes)
    {
        this.ReturnType = ReturnType;
        this.ParameterTypes = ParameterTypes;
        ReturnSignatureType = CliTypeIdentity.FromStackKind(ReturnType);
        // An empty list is the shared empty array: record equality compares arrays by reference.
        var parameters = ParameterTypes.Length == 0 ? [] : new CliTypeIdentity[ParameterTypes.Length];
        for (var index = 0; index < parameters.Length; index++)
            parameters[index] = CliTypeIdentity.FromStackKind(ParameterTypes[index]);
        ParameterSignatureTypes = parameters.Length == 0 ? [] : ImmutableCollectionsMarshal.AsImmutableArray(parameters);
    }

    public MethodSignatureModel(
        CliTypeIdentity returnType,
        ImmutableArray<CliTypeIdentity> parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        parameterTypes = parameterTypes.IsDefault ? [] : parameterTypes;
        ReturnType = returnType.StackKind;
        var kinds = parameterTypes.Length == 0 ? [] : new CliValueKind[parameterTypes.Length];
        for (var index = 0; index < kinds.Length; index++) kinds[index] = parameterTypes[index].StackKind;
        ParameterTypes = kinds.Length == 0 ? [] : ImmutableCollectionsMarshal.AsImmutableArray(kinds);
        ReturnSignatureType = returnType;
        ParameterSignatureTypes = parameterTypes;
    }

    public CliValueKind ReturnType { get; init; }
    public ImmutableArray<CliValueKind> ParameterTypes { get; init; }
    public CliTypeIdentity ReturnSignatureType { get; init; }
    public ImmutableArray<CliTypeIdentity> ParameterSignatureTypes { get; init; }

    // The signature with generic parameters replaced; this signature when none occurs.
    public MethodSignatureModel Substitute(
        ImmutableArray<CliTypeIdentity> typeArguments,
        ImmutableArray<CliTypeIdentity> methodArguments = default)
    {
        var returnType = ReturnSignatureType.Substitute(typeArguments, methodArguments);
        CliTypeIdentity[]? changed = null;
        for (var index = 0; index < ParameterSignatureTypes.Length; index++)
        {
            var parameter = ParameterSignatureTypes[index];
            var substituted = parameter.Substitute(typeArguments, methodArguments);
            if (changed is null && ReferenceEquals(substituted, parameter)) continue;
            if (changed is null)
            {
                changed = new CliTypeIdentity[ParameterSignatureTypes.Length];
                for (var copied = 0; copied < index; copied++) changed[copied] = ParameterSignatureTypes[copied];
            }
            changed[index] = substituted;
        }
        if (changed is null && ReferenceEquals(returnType, ReturnSignatureType))
            return this;
        return new(returnType, changed is null
            ? ParameterSignatureTypes
            : ImmutableCollectionsMarshal.AsImmutableArray(changed));
    }
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
    public bool IsPublic { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsNewSlot { get; init; }
    public bool IsFinal { get; init; }
    public bool IsAbstract { get; init; }
    // PreserveBaseOverridesAttribute: a covariant override that also replaces
    // every slot the method it overrides replaced (ECMA-335 II.10.3.4 as .NET 5
    // extends it).
    public bool PreservesBaseOverrides { get; init; }
    public bool IsAggressiveInlining { get; init; }
    public bool IsNoInlining { get; init; }
    // StackTraceHiddenAttribute on the method or its declaring type.
    public bool IsStackTraceHidden { get; init; }
    public InteropImportDeclaration? JSImport { get; init; }
    public InteropExportDeclaration? JSExport { get; init; }
    public WitImportDeclaration? WitImport { get; init; }
    public WitExportDeclaration? WitExport { get; init; }
    public WitPostReturnDeclaration? WitPostReturn { get; init; }
    public NativeImportDeclaration? NativeImport { get; init; }
    public NativeCallbackDeclaration? NativeCallback { get; init; }
    public UnsafeAccessors.UnsafeAccessorDeclaration? UnsafeAccessor { get; init; }
    public bool IsDelegateAsynchronousCall { get; init; }

    public bool HasBody => RelativeVirtualAddress != 0;

    // Physical metadata readers still use HasBody. Managed analysis may also
    // consume compiler-generated accessor wrappers and the runtime-provided
    // delegate BeginInvoke/EndInvoke bodies, which have no physical RVA.
    public bool HasManagedBody => HasBody || UnsafeAccessor is not null || IsDelegateAsynchronousCall;

    public ImmutableArray<CliValueKind> WasmParameterTypes => IsStatic
        ? Signature.ParameterTypes
        : Signature.ParameterTypes.Insert(0, CliValueKind.ManagedReference);
}

public sealed record PropertyDefinitionModel(
    EntityKey Key,
    EntityKey DeclaringType,
    string Name,
    CliTypeIdentity PropertyType,
    ImmutableArray<CliTypeIdentity> IndexParameterTypes,
    EntityKey? Getter,
    EntityKey? Setter);

public sealed record MethodInstanceModel(
    MethodDefinitionModel Definition,
    CliTypeIdentity DeclaringType,
    ImmutableArray<CliTypeIdentity> MethodArguments,
    MethodSignatureModel Signature)
{
    private readonly DerivedString<(CliTypeIdentity, int, ImmutableArray<CliTypeIdentity>)> _canonicalName = new();

    // A copy made with `with` keeps its own name rather than sharing the original's.
    private MethodInstanceModel(MethodInstanceModel original)
    {
        Definition = original.Definition;
        DeclaringType = original.DeclaringType;
        MethodArguments = original.MethodArguments;
        Signature = original.Signature;
        _canonicalName = new();
    }

    // The compiler keys its tables by this name, so it is built once per instance.
    public string CanonicalName => _canonicalName.Get(
        (DeclaringType, Definition.Key.MetadataToken, MethodArguments),
        static parts => $"{parts.Item1.CanonicalName}::0x{parts.Item2:x8}" +
            (parts.Item3.IsEmpty
                ? ""
                : $"<{string.Join(',', parts.Item3.Select(type => type.CanonicalName))}>"));

    public bool IsConstructed =>
        DeclaringType.Shape is CliTypeShape.GenericInstantiation or CliTypeShape.Array ||
        !MethodArguments.IsEmpty;

    public bool HasEquivalentDescriptorFacts(MethodInstanceModel? other)
    {
        if (other is null)
        {
            return false;
        }

        return Definition.Key == other.Definition.Key &
            Definition.DeclaringType == other.Definition.DeclaringType &
            StringComparer.Ordinal.Equals(Definition.Name, other.Definition.Name) &
            Definition.IsStatic == other.Definition.IsStatic &
            Definition.GenericArity == other.Definition.GenericArity &
            Definition.IsPublic == other.Definition.IsPublic &
            Definition.IsVirtual == other.Definition.IsVirtual &
            Definition.IsNewSlot == other.Definition.IsNewSlot &
            Definition.IsFinal == other.Definition.IsFinal &
            Definition.IsAbstract == other.Definition.IsAbstract &
            Definition.IsAggressiveInlining == other.Definition.IsAggressiveInlining &
            Definition.IsNoInlining == other.Definition.IsNoInlining &
            Definition.IsStackTraceHidden == other.Definition.IsStackTraceHidden &
            Definition.UnsafeAccessor == other.Definition.UnsafeAccessor &
            Definition.NativeImport == other.Definition.NativeImport &
            (Definition.NativeCallback is null
                ? other.Definition.NativeCallback is null
                : Definition.NativeCallback.HasEquivalentFacts(
                    other.Definition.NativeCallback)) &
            HasEquivalentSignature(Definition.Signature, other.Definition.Signature) &
            DeclaringType.Equals(other.DeclaringType) &
            MethodArguments.AsSpan().SequenceEqual(other.MethodArguments.AsSpan()) &
            HasEquivalentSignature(Signature, other.Signature);
    }

    private static bool HasEquivalentSignature(
        MethodSignatureModel left,
        MethodSignatureModel right) =>
        left.ReturnSignatureType.Equals(right.ReturnSignatureType) &
        left.ParameterSignatureTypes.AsSpan().SequenceEqual(
            right.ParameterSignatureTypes.AsSpan());
}

public sealed record FieldInstanceModel(
    FieldDefinitionModel Definition,
    CliTypeIdentity DeclaringType,
    CliTypeIdentity FieldType)
{
    private readonly DerivedString<(CliTypeIdentity, int)> _canonicalName = new();

    private FieldInstanceModel(FieldInstanceModel original)
    {
        Definition = original.Definition;
        DeclaringType = original.DeclaringType;
        FieldType = original.FieldType;
        _canonicalName = new();
    }

    public string CanonicalName => _canonicalName.Get(
        (DeclaringType, Definition.Key.MetadataToken),
        static parts => $"{parts.Item1.CanonicalName}::0x{parts.Item2:x8}");

    public bool IsConstructed =>
        DeclaringType.Shape == CliTypeShape.GenericInstantiation;
}

public sealed record PropertyInstanceModel(
    PropertyDefinitionModel Definition,
    CliTypeIdentity DeclaringType,
    CliTypeIdentity PropertyType,
    ImmutableArray<CliTypeIdentity> IndexParameterTypes,
    MethodInstanceModel? Getter,
    MethodInstanceModel? Setter)
{
    private readonly DerivedString<(CliTypeIdentity, int)> _canonicalName = new();

    private PropertyInstanceModel(PropertyInstanceModel original)
    {
        Definition = original.Definition;
        DeclaringType = original.DeclaringType;
        PropertyType = original.PropertyType;
        IndexParameterTypes = original.IndexParameterTypes;
        Getter = original.Getter;
        Setter = original.Setter;
        _canonicalName = new();
    }

    public string CanonicalName => _canonicalName.Get(
        (DeclaringType, Definition.Key.MetadataToken),
        static parts => $"{parts.Item1.CanonicalName}::0x{parts.Item2:x8}");

    public bool IsConstructed =>
        DeclaringType.Shape == CliTypeShape.GenericInstantiation;

    public bool HasEquivalentDescriptorFacts(PropertyInstanceModel? other)
    {
        if (other is null)
        {
            return false;
        }

        return Definition.Key == other.Definition.Key &
            Definition.DeclaringType == other.Definition.DeclaringType &
            StringComparer.Ordinal.Equals(Definition.Name, other.Definition.Name) &
            Definition.PropertyType.Equals(other.Definition.PropertyType) &
            Definition.IndexParameterTypes.AsSpan().SequenceEqual(
                other.Definition.IndexParameterTypes.AsSpan()) &
            Definition.Getter == other.Definition.Getter &
            Definition.Setter == other.Definition.Setter &
            DeclaringType.Equals(other.DeclaringType) &
            PropertyType.Equals(other.PropertyType) &
            IndexParameterTypes.AsSpan().SequenceEqual(
                other.IndexParameterTypes.AsSpan()) &
            HasEquivalentAccessorFacts(Getter, other.Getter) &
            HasEquivalentAccessorFacts(Setter, other.Setter);
    }

    private static bool HasEquivalentAccessorFacts(
        MethodInstanceModel? left,
        MethodInstanceModel? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.HasEquivalentDescriptorFacts(right);
    }
}

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
    ArrayCloneRange,
    StringCharacterAt,
    StringGetRawDataReference,
    StringEqualsOrdinal,
    StringAllocate,
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
    BoxedValueClone,
    DelegateClone,
    RuntimeHelpersEquals,
    RandomSeed,
    ValueTypeEquals,
    ValueTypeGetHashCode,
    SuppressFinalize,
    ReRegisterForFinalize,
    ReportUnobservedTaskException,
    ExceptionDispatchPreserved,
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
    NativeMemoryClear,
    NativeMemoryCopy,
    NativeMemoryFill,
    ArrayLoopFill,
    ArrayLoopCopy,
    ArrayLoopFillInBounds,
    ArrayLoopCopyInBounds,
    ArrayLoopXor,
    ArrayLoopOr,
    ArrayLoopAnd,
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
    EnumGetMetadata,
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
    IntegerLeadingZeroCount,
    IntegerLog2,
    IntegerPopulationCount,
    IntegerTrailingZeroCount,
    IntegerRotateLeft,
    IntegerRotateRight,
    IntegerDivRem,
    ComponentReallocate,
    ComponentFree,
    ComponentResourceHandleCreate,
    ComponentResourceHandleGet,
    ComponentResourceHandleRelease,
    ObjectArrayDelegateAdapterCreate,
    MemberExecuteMethod,
    DelegateDynamicInvoke,
    MemberReadField,
    EphemeronHandleCreate,
    EphemeronHandleGetKey,
    EphemeronHandleGetValue,
    EphemeronHandleSetTargetToNull,
    EphemeronHandleSetValue,
    EphemeronHandleRelease,
    FloatingCopySign,
    FloatingMaximum,
    FloatingMinimum,
    SimdAdd,
    SimdSubtract,
    SimdMultiply,
    SimdBitwiseAnd,
    SimdBitwiseOr,
    SimdExclusiveOr,
    SimdAndNot,
    SimdOnesComplement,
    SimdNegate,
    SimdAbsolute,
    SimdMinimum,
    SimdMaximum,
    SimdEquals,
    SimdGreaterThan,
    SimdGreaterThanOrEqual,
    SimdLessThan,
    SimdLessThanOrEqual,
    SimdConditionalSelect,
    SimdShuffle,
    SimdShiftLeft,
    SimdShiftRightArithmetic,
    SimdShiftRightLogical,
    SimdEqualsAll,
    SimdNotEqualsAll,
    SimdEqualsAny,
    SimdExtractMostSignificantBits,
    SimdCreateBroadcast,
    SimdCreateElements,
    SimdLoad,
    SimdStore,
    SimdToScalar,
    SimdGetElement,
    SimdWithElement,
    SimdSum,
    SimdDivide,
    SimdSquareRoot,
    SimdFloor,
    SimdCeiling,
    SimdTruncate,
    SimdRound,
    SimdConvert,
    SimdWidenLower,
    SimdWidenUpper,
    SimdNarrow,
    SimdMultiplyAddEstimate,
    SimdCompareAll,
    SimdCompareAny,
    SimdPermute,
    SimdCreateScalar,
    SimdReinterpret,
    PackedSimdIsSupported,
    PackedSimdInstruction,
    NumericsToVector128,
    NumericsFromVector128,
    ObjectContainsReferences,
    ArrayElementType,
    ArraySetValue,
    PreallocatedOutOfMemoryException,
    ObjectClone,
    JavaScriptHost,
}

public static class RuntimeIntrinsicKinds
{
    // The portable Vector128<T> and Vector<T> operations lowered to Wasm SIMD.
    public static bool IsSimdVector(RuntimeIntrinsic intrinsic) =>
        intrinsic is >= RuntimeIntrinsic.SimdAdd and <= RuntimeIntrinsic.SimdReinterpret;
}

public interface IRuntimeIntrinsicRegistry
{
    bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic);
}
