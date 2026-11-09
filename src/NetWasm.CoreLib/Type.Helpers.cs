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

    // Element relationships are not deployed as reflection metadata. A type
    // without an element has the ordinary null result; do not invent that result
    // for arrays, pointers or references whose element cannot be queried.
    public virtual Type? GetElementType() => HasElementType
        ? throw new PlatformNotSupportedException("Runtime element type metadata is not available on NetWasm.")
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
    protected virtual TypeCode GetTypeCodeImpl() => (TypeCode)GetFacts().TypeCode;

    public bool IsPrimitive => IsPrimitiveImpl();

    protected virtual bool IsPrimitiveImpl() => !IsEnum &&
        (GetTypeCodeImpl() is >= TypeCode.Boolean and <= TypeCode.Double ||
         this == typeof(nint) || this == typeof(nuint));

    public static Type? GetType(string typeName) => GetType(typeName, false, false);
    public static Type? GetType(string typeName, bool throwOnError) => GetType(typeName, throwOnError, false);
    public static Type? GetType(string typeName, bool throwOnError, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        throw new PlatformNotSupportedException("Resolving types by name is not supported on NetWasm.");
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

    public virtual Type MakeArrayType() =>
        throw new PlatformNotSupportedException("Constructing runtime types is not supported on NetWasm.");
    public virtual Type MakeArrayType(int rank) =>
        throw new PlatformNotSupportedException("Constructing runtime types is not supported on NetWasm.");
    public virtual Type MakeByRefType() =>
        throw new PlatformNotSupportedException("Constructing runtime types is not supported on NetWasm.");
    public virtual Type MakePointerType() =>
        throw new PlatformNotSupportedException("Constructing runtime types is not supported on NetWasm.");
    public virtual Type MakeGenericType(params Type[] typeArguments) =>
        throw new PlatformNotSupportedException("Constructing runtime types is not supported on NetWasm.");
    public static Type MakeGenericMethodParameter(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        throw new PlatformNotSupportedException("Constructing signature types is not supported on NetWasm.");
    }

    public virtual int GetArrayRank()
    {
        if (!IsArray) throw new ArgumentException("Type must be an array type.");
        return (GetFacts().Flags >> RuntimeTypeFactsFlags.ArrayRankShift) & RuntimeTypeFactsFlags.ArrayRankMask;
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
