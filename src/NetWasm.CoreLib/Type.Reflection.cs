// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Upstream forwarding and validation; runtime member discovery is unavailable.
using System.Reflection;
using System.Globalization;

namespace System;

public partial class Type : IReflect
{
    public virtual Guid GUID => throw UnsupportedTypeOwnerMetadata();
    // The placeholder for the type's assembly, from the type table (see RuntimeAssembly).
    public virtual Assembly Assembly => RuntimeAssembly.FromIndex(RuntimeTypeTable.Assembly(SemanticTypeId))!;
    public new virtual Module Module => Assembly.ManifestModule;
    // NetWasm: the full name with its assembly's name, both from the type-symbols sidecar (issue 225).
    public virtual string? AssemblyQualifiedName =>
        FullName is { } fullName ? fullName + ", " + Assembly.FullName : null;
    // The enclosing type of a nested type, or a generic type parameter's owner, from the type table,
    // whose slot holds the element for an array, pointer or by-reference type instead.
    public override Type? DeclaringType
    {
        get
        {
            if (HasElementType)
            {
                return null;
            }
            int typeId = RuntimeTypeTable.DeclaringType(SemanticTypeId);
            return typeId == 0 ? null : GetTypeFromSemanticId(typeId);
        }
    }
    public override Type? ReflectedType => DeclaringType;
    // A program's types are never unloaded.
    public override bool IsCollectible => false;
    public bool IsNested => DeclaringType != null;
    public override MemberTypes MemberType => (IsPublic || IsNotPublic) ? MemberTypes.TypeInfo : MemberTypes.NestedType;
    public virtual Runtime.InteropServices.StructLayoutAttribute? StructLayoutAttribute => throw UnsupportedTypeOwnerMetadata();
    public virtual bool IsSerializable
    {
        get
        {
            if ((GetAttributeFlagsImpl() & TypeAttributes.Serializable) != 0)
                return true;

            Type? underlyingType = UnderlyingSystemType;
            do
            {
                // In all sane cases we only need to compare the direct level base type with
                // System.Enum and System.MulticastDelegate. However, a generic parameter can
                // have a base type constraint that is Delegate or even a real delegate type.
                // Let's maintain compatibility and return true for them.
                if (underlyingType == typeof(Delegate) || underlyingType == typeof(Enum))
                    return true;

                underlyingType = underlyingType.BaseType;
            }
            while (underlyingType != null);

            return false;
        }
    }

    public bool IsVisible
    {
        get
        {
            if (IsGenericParameter)
                return true;

            if (HasElementType)
                return GetElementType()!.IsVisible;

            Type type = this;
            while (type.IsNested)
            {
                if (!type.IsNestedPublic)
                    return false;

                // this should be null for non-nested types.
                type = type.DeclaringType!;
            }

            // Now "type" should be a top level type
            if (!type.IsPublic)
                return false;

            if (IsGenericType && !IsGenericTypeDefinition)
            {
                foreach (Type t in GetGenericArguments())
                {
                    if (!t.IsVisible)
                        return false;
                }
            }

            return true;
        }
    }
    public static Binder DefaultBinder =>
        throw new PlatformNotSupportedException("Default runtime reflection binding is not supported on NetWasm.");

    private static PlatformNotSupportedException UnsupportedTypeOwnerMetadata() => new(
        "Runtime type owner and attribute metadata is not available on NetWasm.");

    public virtual Type GetGenericTypeDefinition()
    {
        if (!IsGenericType) throw new InvalidOperationException("This operation is only valid on generic types.");
        if (IsGenericTypeDefinition) return this;
        return GetTypeFromSemanticId(RuntimeTypeTable.GenericDefinition(SemanticTypeId));
    }

    // A generic type parameter's place among its owner's parameters, which the type table lists.
    public virtual int GenericParameterPosition
    {
        get
        {
            if (!IsGenericParameter) throw new InvalidOperationException("This type is not a generic parameter.");
            if (IsGenericMethodParameter || DeclaringType is not { } owner)
            {
                throw new PlatformNotSupportedException("Generic method parameter metadata is not available on NetWasm.");
            }
            return Array.IndexOf(owner.GetGenericArguments(), this);
        }
    }

    // A generic parameter's variance and special constraints, which the type table keeps where an
    // array keeps its rank.
    public virtual GenericParameterAttributes GenericParameterAttributes
    {
        get
        {
            if (!IsGenericParameter) throw new InvalidOperationException("This type is not a generic parameter.");
            return (GenericParameterAttributes)((RuntimeTypeTable.Flags(SemanticTypeId) >> RuntimeTypeFactsFlags.ArrayRankShift) &
                RuntimeTypeFactsFlags.ArrayRankMask);
        }
    }

    public virtual MethodBase? DeclaringMethod
    {
        get
        {
            if (!IsGenericParameter) throw new InvalidOperationException("This type is not a generic parameter.");
            if (IsGenericTypeParameter) return null;
            throw new PlatformNotSupportedException("Generic method owner metadata is not available on NetWasm.");
        }
    }

    public virtual Type[] GetGenericParameterConstraints()
    {
        if (!IsGenericParameter) throw new InvalidOperationException("This type is not a generic parameter.");
        throw new PlatformNotSupportedException("Generic parameter constraint metadata is not available on NetWasm.");
    }

    public TypeAttributes Attributes => GetAttributeFlagsImpl();
    // NetWasm: the type's TypeAttributes from the type table.
    protected virtual TypeAttributes GetAttributeFlagsImpl() => (TypeAttributes)RuntimeTypeTable.Attributes(SemanticTypeId);

    public bool IsImport => (GetAttributeFlagsImpl() & TypeAttributes.Import) != 0;
    public bool IsSpecialName => (GetAttributeFlagsImpl() & TypeAttributes.SpecialName) != 0;
    public bool IsNestedAssembly => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NestedAssembly;
    public bool IsNestedFamANDAssem => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamANDAssem;
    public bool IsNestedFamily => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamily;
    public bool IsNestedFamORAssem => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamORAssem;
    public bool IsNestedPrivate => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPrivate;
    public bool IsNestedPublic => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;
    public bool IsNotPublic => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.NotPublic;
    public bool IsPublic => (GetAttributeFlagsImpl() & TypeAttributes.VisibilityMask) == TypeAttributes.Public;
    public bool IsAutoLayout => (GetAttributeFlagsImpl() & TypeAttributes.LayoutMask) == TypeAttributes.AutoLayout;
    public bool IsExplicitLayout => (GetAttributeFlagsImpl() & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout;
    public bool IsLayoutSequential => (GetAttributeFlagsImpl() & TypeAttributes.LayoutMask) == TypeAttributes.SequentialLayout;
    public bool IsAnsiClass => (GetAttributeFlagsImpl() & TypeAttributes.StringFormatMask) == TypeAttributes.AnsiClass;
    public bool IsAutoClass => (GetAttributeFlagsImpl() & TypeAttributes.StringFormatMask) == TypeAttributes.AutoClass;
    public bool IsUnicodeClass => (GetAttributeFlagsImpl() & TypeAttributes.StringFormatMask) == TypeAttributes.UnicodeClass;

    public static readonly MemberFilter FilterAttribute = FilterAttributeImpl!;

    private static bool FilterAttributeImpl(MemberInfo m, object filterCriteria)
    {
        // Check that the criteria object is an Integer object
        if (filterCriteria == null)
            throw new InvalidFilterCriteriaException("Filter criteria must be an integer.");

        switch (m.MemberType)
        {
            case MemberTypes.Constructor:
            case MemberTypes.Method:
                {
                    MethodAttributes criteria;
                    try
                    {
                        int i = (int)filterCriteria;
                        criteria = (MethodAttributes)i;
                    }
                    catch
                    {
                        throw new InvalidFilterCriteriaException("Filter criteria must be an integer.");
                    }


                    MethodAttributes attr;
                    if (m.MemberType == MemberTypes.Method)
                        attr = ((MethodInfo)m).Attributes;
                    else
                        attr = ((ConstructorInfo)m).Attributes;

                    if (((criteria & MethodAttributes.MemberAccessMask) != 0) && (attr & MethodAttributes.MemberAccessMask) != (criteria & MethodAttributes.MemberAccessMask))
                        return false;
                    if (((criteria & MethodAttributes.Static) != 0) && (attr & MethodAttributes.Static) == 0)
                        return false;
                    if (((criteria & MethodAttributes.Final) != 0) && (attr & MethodAttributes.Final) == 0)
                        return false;
                    if (((criteria & MethodAttributes.Virtual) != 0) && (attr & MethodAttributes.Virtual) == 0)
                        return false;
                    if (((criteria & MethodAttributes.Abstract) != 0) && (attr & MethodAttributes.Abstract) == 0)
                        return false;
                    if (((criteria & MethodAttributes.SpecialName) != 0) && (attr & MethodAttributes.SpecialName) == 0)
                        return false;
                    return true;
                }
            case MemberTypes.Field:
                {
                    FieldAttributes criteria;
                    try
                    {
                        int i = (int)filterCriteria;
                        criteria = (FieldAttributes)i;
                    }
                    catch
                    {
                        throw new InvalidFilterCriteriaException("Filter criteria must be an integer.");
                    }

                    FieldAttributes attr = ((FieldInfo)m).Attributes;
                    if (((criteria & FieldAttributes.FieldAccessMask) != 0) && (attr & FieldAttributes.FieldAccessMask) != (criteria & FieldAttributes.FieldAccessMask))
                        return false;
                    if (((criteria & FieldAttributes.Static) != 0) && (attr & FieldAttributes.Static) == 0)
                        return false;
                    if (((criteria & FieldAttributes.InitOnly) != 0) && (attr & FieldAttributes.InitOnly) == 0)
                        return false;
                    if (((criteria & FieldAttributes.Literal) != 0) && (attr & FieldAttributes.Literal) == 0)
                        return false;
#pragma warning disable SYSLIB0050 // Legacy serialization infrastructure is obsolete
                    if (((criteria & FieldAttributes.NotSerialized) != 0) && (attr & FieldAttributes.NotSerialized) == 0)
                        return false;
#pragma warning restore SYSLIB0050
                    if (((criteria & FieldAttributes.PinvokeImpl) != 0) && (attr & FieldAttributes.PinvokeImpl) == 0)
                        return false;
                    return true;
                }
        }

        return false;
    }

    public static readonly MemberFilter FilterName = (member, criteria) => FilterNameImpl(member, criteria!, StringComparison.Ordinal);
    public static readonly MemberFilter FilterNameIgnoreCase = (member, criteria) => FilterNameImpl(member, criteria!, StringComparison.OrdinalIgnoreCase);

    private static bool FilterNameImpl(MemberInfo m, object filterCriteria, StringComparison comparison)
    {
        // Check that the criteria object is a String object
        if (filterCriteria is not string filterCriteriaString)
        {
            throw new InvalidFilterCriteriaException("Filter criteria must be a string.");
        }

        ReadOnlySpan<char> str = filterCriteriaString.AsSpan().Trim();
        ReadOnlySpan<char> name = m.Name;

        // Get the nested class name only, as opposed to the mangled one
        if (m.MemberType == MemberTypes.NestedType)
        {
            name = name.Slice(name.LastIndexOf('+') + 1);
        }

        // Check to see if this is a prefix or exact match requirement
        if (str.EndsWith('*'))
        {
            str = str.Slice(0, str.Length - 1);
            return name.StartsWith(str, comparison);
        }

        return name.Equals(str, comparison);
    }

    public Type? GetInterface(string name) => GetInterface(name, false);
    // NetWasm: matched by the Name, or FullName where the name has a namespace, that the type-symbols
    // sidecar supplies; unavailable without the names.
    public virtual Type? GetInterface(string name, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TypeSymbols.Available) throw TypeSymbols.NamesUnavailable();
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool qualified = name.Contains('.');
        Type? match = null;
        foreach (Type candidate in GetInterfaces())
        {
            if (string.Equals(qualified ? candidate.FullName : candidate.Name, name, comparison))
            {
                if (match is not null) throw new AmbiguousMatchException();
                match = candidate;
            }
        }
        return match;
    }

    // NetWasm: the interfaces the program uses that the type implements, from the type table: its own
    // and its base types', in type ID order; one nothing in the program uses is not there.
    public virtual Type[] GetInterfaces()
    {
        int[] ids = new int[8];
        int count = 0;
        for (Type? type = this; type is not null; type = type.BaseType)
        {
            foreach (int id in RuntimeTypeTable.Interfaces(type.SemanticTypeId))
            {
                bool seen = false;
                for (int index = 0; index < count && !seen; index++)
                {
                    seen = ids[index] == id;
                }
                if (seen)
                {
                    continue;
                }
                if (count == ids.Length)
                {
                    int[] grown = new int[count * 2];
                    ids.CopyTo(grown, 0);
                    ids = grown;
                }
                ids[count++] = id;
            }
        }
        return TypesOf(ids, count);
    }
    public virtual InterfaceMapping GetInterfaceMap(Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(interfaceType);
        throw new PlatformNotSupportedException("Interface method discovery is not supported on NetWasm.");
    }

    public virtual Type[] FindInterfaces(TypeFilter filter, object? filterCriteria)
    {
        ArgumentNullException.ThrowIfNull(filter);

        Type?[] c = GetInterfaces();
        int cnt = 0;
        for (int i = 0; i < c.Length; i++)
        {
            if (!filter(c[i]!, filterCriteria))
                c[i] = null;
            else
                cnt++;
        }
        if (cnt == c.Length)
            return c!;

        Type[] ret = new Type[cnt];
        cnt = 0;
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i] is Type t)
                ret[cnt++] = t;
        }
        return ret;
    }

    public virtual MemberInfo[] FindMembers(MemberTypes memberType, BindingFlags bindingAttr, MemberFilter? filter, object? filterCriteria)
    {
        // Define the work arrays
        MethodInfo?[]? m = null;
        ConstructorInfo?[]? c = null;
        FieldInfo?[]? f = null;
        PropertyInfo?[]? p = null;
        EventInfo?[]? e = null;
        Type?[]? t = null;

        int i;
        int cnt = 0;            // Total Matchs

        // Check the methods
        if ((memberType & MemberTypes.Method) != 0)
        {
            m = GetMethods(bindingAttr);
            if (filter != null)
            {
                for (i = 0; i < m.Length; i++)
                    if (!filter(m[i]!, filterCriteria))
                        m[i] = null;
                    else
                        cnt++;
            }
            else
            {
                cnt += m.Length;
            }
        }

        // Check the constructors
        if ((memberType & MemberTypes.Constructor) != 0)
        {
            c = GetConstructors(bindingAttr);
            if (filter != null)
            {
                for (i = 0; i < c.Length; i++)
                    if (!filter(c[i]!, filterCriteria))
                        c[i] = null;
                    else
                        cnt++;
            }
            else
            {
                cnt += c.Length;
            }
        }

        // Check the fields
        if ((memberType & MemberTypes.Field) != 0)
        {
            f = GetFields(bindingAttr);
            if (filter != null)
            {
                for (i = 0; i < f.Length; i++)
                    if (!filter(f[i]!, filterCriteria))
                        f[i] = null;
                    else
                        cnt++;
            }
            else
            {
                cnt += f.Length;
            }
        }

        // Check the Properties
        if ((memberType & MemberTypes.Property) != 0)
        {
            p = GetProperties(bindingAttr);
            if (filter != null)
            {
                for (i = 0; i < p.Length; i++)
                    if (!filter(p[i]!, filterCriteria))
                        p[i] = null;
                    else
                        cnt++;
            }
            else
            {
                cnt += p.Length;
            }
        }

        // Check the Events
        if ((memberType & MemberTypes.Event) != 0)
        {
            e = GetEvents(bindingAttr);
            if (filter != null)
            {
                for (i = 0; i < e.Length; i++)
                    if (!filter(e[i]!, filterCriteria))
                        e[i] = null;
                    else
                        cnt++;
            }
            else
            {
                cnt += e.Length;
            }
        }

        // Check the Types
        if ((memberType & MemberTypes.NestedType) != 0)
        {
            t = GetNestedTypes(bindingAttr);
            if (filter != null)
            {
                for (i = 0; i < t.Length; i++)
                    if (!filter(t[i]!, filterCriteria))
                        t[i] = null;
                    else
                        cnt++;
            }
            else
            {
                cnt += t.Length;
            }
        }

        // Allocate the Member Info
        MemberInfo[] ret = new MemberInfo[cnt];

        // Copy the Methods
        cnt = 0;
        if (m != null)
        {
            for (i = 0; i < m.Length; i++)
                if (m[i] != null)
                    ret[cnt++] = m[i]!;
        }

        // Copy the Constructors
        if (c != null)
        {
            for (i = 0; i < c.Length; i++)
                if (c[i] is ConstructorInfo ci)
                    ret[cnt++] = ci;
        }

        // Copy the Fields
        if (f != null)
        {
            for (i = 0; i < f.Length; i++)
                if (f[i] is FieldInfo fi)
                    ret[cnt++] = fi;
        }

        // Copy the Properties
        if (p != null)
        {
            for (i = 0; i < p.Length; i++)
                if (p[i] is PropertyInfo pi)
                    ret[cnt++] = pi;
        }

        // Copy the Events
        if (e != null)
        {
            for (i = 0; i < e.Length; i++)
                if (e[i] is EventInfo ei)
                    ret[cnt++] = ei;
        }

        // Copy the Types
        if (t != null)
        {
            for (i = 0; i < t.Length; i++)
                if (t[i] is Type type)
                    ret[cnt++] = type;
        }

        return ret;
    }

    public ConstructorInfo? TypeInitializer
    {
        get => GetConstructorImpl(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, CallingConventions.Any, [], null);
    }

    public ConstructorInfo? GetConstructor(Type[] types) => GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, types, null);

    public ConstructorInfo? GetConstructor(BindingFlags bindingAttr, Type[] types) => GetConstructor(bindingAttr, binder: null, types, modifiers: null);

    public ConstructorInfo? GetConstructor(BindingFlags bindingAttr, Binder? binder, Type[] types, ParameterModifier[]? modifiers) => GetConstructor(bindingAttr, binder, CallingConventions.Any, types, modifiers);

    public ConstructorInfo? GetConstructor(BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[] types, ParameterModifier[]? modifiers)
    {
        ArgumentNullException.ThrowIfNull(types);

        for (int i = 0; i < types.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(types[i], nameof(types));
        }
        return GetConstructorImpl(bindingAttr, binder, callConvention, types, modifiers);
    }

    protected virtual ConstructorInfo? GetConstructorImpl(BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[] types, ParameterModifier[]? modifiers) => throw UnsupportedMemberLookup();

    public ConstructorInfo[] GetConstructors() => GetConstructors(BindingFlags.Public | BindingFlags.Instance);

    public virtual ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public EventInfo? GetEvent(string name) => GetEvent(name, DefaultLookup);

    public virtual EventInfo? GetEvent(string name, BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public virtual EventInfo[] GetEvents() => GetEvents(DefaultLookup);

    public virtual EventInfo[] GetEvents(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public FieldInfo? GetField(string name) => GetField(name, DefaultLookup);

    public virtual FieldInfo? GetField(string name, BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public FieldInfo[] GetFields() => GetFields(DefaultLookup);

    public virtual FieldInfo[] GetFields(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public MemberInfo[] GetMember(string name) => GetMember(name, DefaultLookup);

    public virtual MemberInfo[] GetMember(string name, BindingFlags bindingAttr) => GetMember(name, MemberTypes.All, bindingAttr);

    public virtual MemberInfo[] GetMember(string name, MemberTypes type, BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public MemberInfo[] GetMembers() => GetMembers(DefaultLookup);

    public virtual MemberInfo[] GetMembers(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public MethodInfo? GetMethod(string name) => GetMethod(name, DefaultLookup);

    public MethodInfo? GetMethod(string name, BindingFlags bindingAttr)
    {
        ArgumentNullException.ThrowIfNull(name);

        return GetMethodImpl(name, bindingAttr, null, CallingConventions.Any, null, null);
    }

    public MethodInfo? GetMethod(string name, BindingFlags bindingAttr, Type[] types) => GetMethod(name, bindingAttr, binder: null, types, modifiers: null);

    public MethodInfo? GetMethod(string name, Type[] types) => GetMethod(name, types, null);

    public MethodInfo? GetMethod(string name, Type[] types, ParameterModifier[]? modifiers) => GetMethod(name, DefaultLookup, null, types, modifiers);

    public MethodInfo? GetMethod(string name, BindingFlags bindingAttr, Binder? binder, Type[] types, ParameterModifier[]? modifiers) => GetMethod(name, bindingAttr, binder, CallingConventions.Any, types, modifiers);

    public MethodInfo? GetMethod(string name, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[] types, ParameterModifier[]? modifiers)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(types);

        for (int i = 0; i < types.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(types[i], nameof(types));
        }
        return GetMethodImpl(name, bindingAttr, binder, callConvention, types, modifiers);
    }

    protected virtual MethodInfo? GetMethodImpl(string name, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[]? types, ParameterModifier[]? modifiers) => throw UnsupportedMemberLookup();

    public MethodInfo? GetMethod(string name, int genericParameterCount, Type[] types) => GetMethod(name, genericParameterCount, types, null);

    public MethodInfo? GetMethod(string name, int genericParameterCount, Type[] types, ParameterModifier[]? modifiers) => GetMethod(name, genericParameterCount, DefaultLookup, null, types, modifiers);

    public MethodInfo? GetMethod(string name, int genericParameterCount, BindingFlags bindingAttr, Type[] types) => GetMethod(name, genericParameterCount, bindingAttr, null, types, null);

    public MethodInfo? GetMethod(string name, int genericParameterCount, BindingFlags bindingAttr, Binder? binder, Type[] types, ParameterModifier[]? modifiers) => GetMethod(name, genericParameterCount, bindingAttr, binder, CallingConventions.Any, types, modifiers);

    public MethodInfo? GetMethod(string name, int genericParameterCount, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[] types, ParameterModifier[]? modifiers)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(genericParameterCount);
        ArgumentNullException.ThrowIfNull(types);
        for (int i = 0; i < types.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(types[i], nameof(types));
        }
        return GetMethodImpl(name, genericParameterCount, bindingAttr, binder, callConvention, types, modifiers);
    }

    protected virtual MethodInfo? GetMethodImpl(string name, int genericParameterCount, BindingFlags bindingAttr, Binder? binder, CallingConventions callConvention, Type[]? types, ParameterModifier[]? modifiers) => throw UnsupportedMemberLookup();

    public MethodInfo[] GetMethods() => GetMethods(DefaultLookup);

    public virtual MethodInfo[] GetMethods(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public Type? GetNestedType(string name) => GetNestedType(name, DefaultLookup);

    // NetWasm: matched by the Name the type-symbols sidecar supplies; unavailable without the names.
    public virtual Type? GetNestedType(string name, BindingFlags bindingAttr)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TypeSymbols.Available) throw TypeSymbols.NamesUnavailable();
        StringComparison comparison = (bindingAttr & BindingFlags.IgnoreCase) != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Type? match = null;
        foreach (Type nested in GetNestedTypes(bindingAttr))
        {
            if (string.Equals(nested.Name, name, comparison))
            {
                if (match is not null) throw new AmbiguousMatchException();
                match = nested;
            }
        }
        return match;
    }

    public Type[] GetNestedTypes() => GetNestedTypes(DefaultLookup);

    // NetWasm: the nested type definitions the program has whose declaring type is this type's
    // definition, from the type table, in type ID order; one nothing in the program uses is not there.
    public virtual Type[] GetNestedTypes(BindingFlags bindingAttr)
    {
        bool publicTypes = (bindingAttr & BindingFlags.Public) != 0;
        bool nonPublicTypes = (bindingAttr & BindingFlags.NonPublic) != 0;
        if (HasElementType || IsGenericParameter || !publicTypes && !nonPublicTypes)
        {
            return [];
        }
        int owner = IsGenericType && !IsGenericTypeDefinition ? GetGenericTypeDefinition().SemanticTypeId : SemanticTypeId;
        int[] ids = new int[RuntimeTypeTable.Capacity()];
        int count = 0;
        for (int id = 1; id < ids.Length; id++)
        {
            if ((RuntimeTypeTable.Flags(id) & RuntimeTypeFactsFlags.TypeDefinition) != 0 && RuntimeTypeTable.DeclaringType(id) == owner &&
                (GetTypeFromSemanticId(id).IsNestedPublic ? publicTypes : nonPublicTypes))
            {
                ids[count++] = id;
            }
        }
        return TypesOf(ids, count);
    }

    public PropertyInfo? GetProperty(string name) => GetProperty(name, DefaultLookup);

    public PropertyInfo? GetProperty(string name, BindingFlags bindingAttr)
    {
        ArgumentNullException.ThrowIfNull(name);

        return GetPropertyImpl(name, bindingAttr, null, null, null, null);
    }

    public PropertyInfo? GetProperty(string name, Type? returnType)
    {
        ArgumentNullException.ThrowIfNull(name);

        return GetPropertyImpl(name, DefaultLookup, null, returnType, null, null);
    }

    public PropertyInfo? GetProperty(string name, Type[] types) => GetProperty(name, null, types);

    public PropertyInfo? GetProperty(string name, Type? returnType, Type[] types) => GetProperty(name, returnType, types, null);

    public PropertyInfo? GetProperty(string name, Type? returnType, Type[] types, ParameterModifier[]? modifiers) => GetProperty(name, DefaultLookup, null, returnType, types, modifiers);

    public PropertyInfo? GetProperty(string name, BindingFlags bindingAttr, Binder? binder, Type? returnType, Type[] types, ParameterModifier[]? modifiers)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(types);

        return GetPropertyImpl(name, bindingAttr, binder, returnType, types, modifiers);
    }

    protected virtual PropertyInfo? GetPropertyImpl(string name, BindingFlags bindingAttr, Binder? binder, Type? returnType, Type[]? types, ParameterModifier[]? modifiers) => throw UnsupportedMemberLookup();

    public PropertyInfo[] GetProperties() => GetProperties(DefaultLookup);

    public virtual PropertyInfo[] GetProperties(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public virtual MemberInfo[] GetDefaultMembers() => throw UnsupportedMemberLookup();

    // Canonical Type objects represent their own runtime type identity.
    public virtual Type UnderlyingSystemType => this;

    public object? InvokeMember(string name, BindingFlags invokeAttr, Binder? binder, object? target, object?[]? args) =>
        InvokeMember(name, invokeAttr, binder, target, args, null, null, null);

    public object? InvokeMember(string name, BindingFlags invokeAttr, Binder? binder, object? target, object?[]? args, CultureInfo? culture) =>
        InvokeMember(name, invokeAttr, binder, target, args, null, culture, null);

    public virtual object? InvokeMember(string name, BindingFlags invokeAttr, Binder? binder, object? target, object?[]? args, ParameterModifier[]? modifiers, CultureInfo? culture, string[]? namedParameters) =>
        throw UnsupportedMemberLookup();

    internal const BindingFlags DefaultLookup = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

    private static PlatformNotSupportedException UnsupportedMemberLookup() => new(
        "Runtime member lookup is unavailable because reflection metadata is not deployed.");
}
