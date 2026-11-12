// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Managed Type helpers; shape predicates use existing canonical runtime facts.
namespace System;

public partial class Type
{
    public static readonly char Delimiter = '.';
    public static readonly object Missing = Reflection.Missing.Value;

    public virtual bool Equals(Type? o) => o != null && ReferenceEquals(this.UnderlyingSystemType, o.UnderlyingSystemType);

    public virtual Type[] GenericTypeArguments => (IsGenericType && !IsGenericTypeDefinition) ? GetGenericArguments() : [];

    public virtual bool IsConstructedGenericType => IsGenericType && !IsGenericTypeDefinition;

    public virtual bool IsVariableBoundArray => IsArray && !IsSZArray;

    public bool HasElementType => HasElementTypeImpl();

    protected virtual bool HasElementTypeImpl() => IsArray || IsByRef || IsPointer;

    // An array's, pointer's or by-reference type's element, from the type table.
    public virtual bool IsFunctionPointer => false;
    public virtual bool IsUnmanagedFunctionPointer => false;

    public virtual Array GetEnumValuesAsUnderlyingType()
    {
        if (!IsEnum) throw new ArgumentException("Type provided must be an Enum.", "enumType");
        return Enum.GetValuesAsUnderlyingType(this);
    }

    public virtual Type? GetElementType() => HasElementType
        ? GetTypeFromSemanticId(RuntimeTypeTable.ElementType(SemanticTypeId))
        : null;


    // Canonical Type objects use NetWasm's existing enum metadata operations.
    // Upstream Type.Enum's fallback discovers fields for user-defined Type
    // subclasses; those providers are not constructible on this runtime.
    public virtual string? GetEnumName(object value) => Enum.GetName(this, value);
    public virtual string[] GetEnumNames() => Enum.GetNames(this);
    public virtual Array GetEnumValues() => Enum.GetValues(this);
    public virtual Type GetEnumUnderlyingType() => Enum.GetUnderlyingType(this);
    public virtual bool IsEnumDefined(object value) => Enum.IsDefined(this, value);

    // The canonical Type supplies runtime facts directly. Upstream exposes
    // these hooks on Type and implements them on its RuntimeType subclass.
    protected virtual bool IsArrayImpl() => HasFlag(RuntimeTypeFactsFlags.Array);
    protected virtual bool IsByRefImpl() => HasFlag(RuntimeTypeFactsFlags.ByReference);
    protected virtual bool IsPointerImpl() => HasFlag(RuntimeTypeFactsFlags.Pointer);
    protected virtual bool IsValueTypeImpl() => HasFlag(RuntimeTypeFactsFlags.ValueType);
    protected virtual TypeCode GetTypeCodeImpl() => (TypeCode)(RuntimeTypeTable.Flags(SemanticTypeId) >> 24 & 0x1f);

    public bool IsPrimitive => IsPrimitiveImpl();

    protected virtual bool IsPrimitiveImpl() => !IsEnum &&
        (GetTypeCodeImpl() is >= TypeCode.Boolean and <= TypeCode.Double ||
         this == typeof(nint) || this == typeof(nuint));

    public static Type? GetType(string typeName) => GetType(typeName, false, false);
    public static Type? GetType(string typeName, bool throwOnError) => GetType(typeName, throwOnError, false);
    // NetWasm: the program's type with that FullName or AssemblyQualifiedName, which the type-symbols
    // sidecar supplies; without the names the lookup is unavailable. A type nothing in the program uses
    // is not found, and names are matched whole, not parsed.
    public static Type? GetType(string typeName, bool throwOnError, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        return FindByName(typeName, throwOnError, ignoreCase, assembly: null);
    }

    internal static Type? FindByName(string name, bool throwOnError, bool ignoreCase, Reflection.Assembly? assembly)
    {
        if (!TypeSymbols.Available)
        {
            throw TypeSymbols.NamesUnavailable();
        }
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int capacity = RuntimeTypeTable.Capacity();
        for (int id = 1; id < capacity; id++)
        {
            if (!RuntimeTypeTable.IsRegistered(id))
            {
                continue;
            }
            Type type = GetTypeFromSemanticId(id);
            if ((assembly is null || type.Assembly == assembly) &&
                (string.Equals(type.FullName, name, comparison) || string.Equals(type.AssemblyQualifiedName, name, comparison)))
            {
                return type;
            }
        }
        return throwOnError ? throw new TypeLoadException("Could not load type '" + name + "'.") : null;
    }

    public static Type? GetType(string typeName,
        Func<Reflection.AssemblyName, Reflection.Assembly?>? assemblyResolver,
        Func<Reflection.Assembly?, string, bool, Type?>? typeResolver) =>
        GetType(typeName, assemblyResolver, typeResolver, false, false);

    public static Type? GetType(string typeName,
        Func<Reflection.AssemblyName, Reflection.Assembly?>? assemblyResolver,
        Func<Reflection.Assembly?, string, bool, Type?>? typeResolver, bool throwOnError) =>
        GetType(typeName, assemblyResolver, typeResolver, throwOnError, false);

    public static Type? GetType(string typeName,
        Func<Reflection.AssemblyName, Reflection.Assembly?>? assemblyResolver,
        Func<Reflection.Assembly?, string, bool, Type?>? typeResolver, bool throwOnError, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        throw new PlatformNotSupportedException("Resolving types by name is not supported on NetWasm.");
    }

    public static Type? ReflectionOnlyGetType(string typeName, bool throwIfNotFound, bool ignoreCase) =>
        throw new PlatformNotSupportedException("Reflection-only type loading is not supported on NetWasm.");

    public new Type GetType() => base.GetType();
    public virtual bool IsSignatureType => false;
    public virtual bool IsTypeDefinition => HasFlag(RuntimeTypeFactsFlags.TypeDefinition);
    // Modern managed runtimes do not enforce Code Access Security transparency.
    public virtual bool IsSecurityCritical => true;
    public virtual bool IsSecuritySafeCritical => false;
    public virtual bool IsSecurityTransparent => false;
    public virtual bool IsByRefLike => HasFlag(RuntimeTypeFactsFlags.ByRefLike);
    public virtual bool IsGenericParameter => HasFlag(RuntimeTypeFactsFlags.GenericTypeParameter | RuntimeTypeFactsFlags.GenericMethodParameter);
    public virtual bool IsGenericTypeParameter => HasFlag(RuntimeTypeFactsFlags.GenericTypeParameter);
    public virtual bool IsGenericMethodParameter => HasFlag(RuntimeTypeFactsFlags.GenericMethodParameter);

    public bool IsMarshalByRef => IsMarshalByRefImpl();
    protected virtual bool IsMarshalByRefImpl() => false;

    public bool IsCOMObject => IsCOMObjectImpl();
    protected virtual bool IsCOMObjectImpl() => false;
    public bool IsContextful => IsContextfulImpl();
    protected virtual bool IsContextfulImpl() => false;

    public static Type? GetTypeFromCLSID(Guid clsid) => GetTypeFromCLSID(clsid, null, false);
    public static Type? GetTypeFromCLSID(Guid clsid, bool throwOnError) => GetTypeFromCLSID(clsid, null, throwOnError);
    public static Type? GetTypeFromCLSID(Guid clsid, string? server) => GetTypeFromCLSID(clsid, server, false);
    public static Type? GetTypeFromCLSID(Guid clsid, string? server, bool throwOnError) =>
        throw new PlatformNotSupportedException("COM type lookup is not supported on NetWasm.");

    public static Type? GetTypeFromProgID(string progID) => GetTypeFromProgID(progID, null, false);
    public static Type? GetTypeFromProgID(string progID, bool throwOnError) => GetTypeFromProgID(progID, null, throwOnError);
    public static Type? GetTypeFromProgID(string progID, string? server) => GetTypeFromProgID(progID, server, false);
    public static Type? GetTypeFromProgID(string progID, string? server, bool throwOnError) =>
        throw new PlatformNotSupportedException("COM type lookup is not supported on NetWasm.");

    // NetWasm: a program cannot create types, so these return the type the program has, from the type
    // table, and throw where it has none.
    public virtual Type MakeArrayType() => WithElement(RuntimeTypeFactsFlags.SzArray, 1);

    public virtual Type MakeArrayType(int rank)
    {
        if (rank <= 0) throw new IndexOutOfRangeException();
        return WithElement(RuntimeTypeFactsFlags.Array, rank, RuntimeTypeFactsFlags.SzArray);
    }

    public virtual Type MakeByRefType() => WithElement(RuntimeTypeFactsFlags.ByReference, 0);

    public virtual Type MakePointerType() => WithElement(RuntimeTypeFactsFlags.Pointer, 0);

    public virtual Type MakeGenericType(params Type[] typeArguments)
    {
        ArgumentNullException.ThrowIfNull(typeArguments);
        if (!IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(this + " is not a GenericTypeDefinition. MakeGenericType may only be called on a type for which Type.IsGenericTypeDefinition is true.");
        }
        int[] arguments = new int[typeArguments.Length];
        for (int index = 0; index < arguments.Length; index++)
        {
            arguments[index] = (typeArguments[index] ?? throw new ArgumentNullException(nameof(typeArguments))).SemanticTypeId;
        }
        if (arguments.Length != GetGenericArguments().Length)
        {
            throw new ArgumentException("The number of generic arguments provided doesn't equal the arity of the generic type definition.", "instantiation");
        }
        for (int id = 1; id < RuntimeTypeTable.Capacity(); id++)
        {
            if ((RuntimeTypeTable.Flags(id) & (RuntimeTypeFactsFlags.GenericType | RuntimeTypeFactsFlags.GenericTypeDefinition)) ==
                    RuntimeTypeFactsFlags.GenericType &&
                RuntimeTypeTable.GenericDefinition(id) == SemanticTypeId &&
                SameIds(RuntimeTypeTable.GenericArguments(id), arguments))
            {
                return GetTypeFromSemanticId(id);
            }
        }
        throw NotInProgram();
    }

    private static bool SameIds(int[]? left, int[] right)
    {
        if (left is null || left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index]) return false;
        }
        return true;
    }

    // The types with the given IDs, in order; the first count of them.
    internal static Type[] TypesOf(int[] ids, int count)
    {
        var types = new Type[count];
        for (int index = 0; index < count; index++)
        {
            types[index] = GetTypeFromSemanticId(ids[index]);
        }
        return types;
    }

    // The program's type of the given kind (and rank, for an array) whose element is this type.
    private Type WithElement(int kind, int rank, int excluded = 0)
    {
        for (int id = 1; id < RuntimeTypeTable.Capacity(); id++)
        {
            int flags = RuntimeTypeTable.Flags(id);
            if ((flags & (kind | excluded)) == kind &&
                (rank == 0 || (flags >> RuntimeTypeFactsFlags.ArrayRankShift & RuntimeTypeFactsFlags.ArrayRankMask) == rank) &&
                RuntimeTypeTable.ElementType(id) == SemanticTypeId)
            {
                return GetTypeFromSemanticId(id);
            }
        }
        throw NotInProgram();
    }

    private static PlatformNotSupportedException NotInProgram() =>
        new("The program does not use this type, and NetWasm cannot create types at run time.");
    public static Type MakeGenericMethodParameter(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        throw new PlatformNotSupportedException("Constructing signature types is not supported on NetWasm.");
    }

    public virtual int GetArrayRank()
    {
        if (!IsArray) throw new ArgumentException("Type must be an array type.");
        return (RuntimeTypeTable.Flags(SemanticTypeId) >> RuntimeTypeFactsFlags.ArrayRankShift) & RuntimeTypeFactsFlags.ArrayRankMask;
    }

    public static Type[] GetTypeArray(object[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Type[] cls = new Type[args.Length];
        for (int i = 0; i < cls.Length; i++)
        {
            if (args[i] == null)
                throw new ArgumentException("At least one element in the specified array was null.", nameof(args));
            cls[i] = args[i].GetType();
        }
        return cls;
    }
}
