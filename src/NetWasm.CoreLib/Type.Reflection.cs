// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Upstream forwarding and validation; runtime member discovery is unavailable.
using System.Reflection;
using System.Globalization;

namespace System;

public partial class Type : IReflect
{
    public virtual Guid GUID => throw UnsupportedTypeOwnerMetadata();
    public virtual Assembly Assembly => throw UnsupportedTypeOwnerMetadata();
    public new virtual Module Module => throw UnsupportedTypeOwnerMetadata();
    public virtual string? AssemblyQualifiedName => throw UnsupportedTypeOwnerMetadata();
    public override Type? DeclaringType => throw UnsupportedTypeOwnerMetadata();
    public override Type? ReflectedType => DeclaringType;
    public bool IsNested => DeclaringType != null;
    public override MemberTypes MemberType => (IsPublic || IsNotPublic) ? MemberTypes.TypeInfo : MemberTypes.NestedType;
    public virtual Runtime.InteropServices.StructLayoutAttribute? StructLayoutAttribute => throw UnsupportedTypeOwnerMetadata();
    public virtual bool IsSerializable => throw UnsupportedTypeOwnerMetadata();
    public bool IsVisible => throw UnsupportedTypeOwnerMetadata();
    public static Binder DefaultBinder =>
        throw new PlatformNotSupportedException("Default runtime reflection binding is not supported on NetWasm.");

    private static PlatformNotSupportedException UnsupportedTypeOwnerMetadata() => new(
        "Runtime type owner and attribute metadata is not available on NetWasm.");

    public virtual Type GetGenericTypeDefinition()
    {
        if (!IsGenericType) throw new InvalidOperationException("This type is not generic.");
        if (IsGenericTypeDefinition) return this;
        throw new PlatformNotSupportedException("Generic definition discovery is not supported on NetWasm.");
    }

    public virtual int GenericParameterPosition
    {
        get
        {
            if (!IsGenericParameter) throw new InvalidOperationException("This type is not a generic parameter.");
            throw new PlatformNotSupportedException("Generic parameter position metadata is not available on NetWasm.");
        }
    }

    public virtual GenericParameterAttributes GenericParameterAttributes
    {
        get
        {
            if (!IsGenericParameter) throw new InvalidOperationException("This type is not a generic parameter.");
            throw new PlatformNotSupportedException("Generic parameter attribute metadata is not available on NetWasm.");
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
    protected virtual TypeAttributes GetAttributeFlagsImpl() =>
        throw new PlatformNotSupportedException("Runtime type attribute metadata is not available on NetWasm.");

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
    public virtual Type? GetInterface(string name, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(name);
        throw new PlatformNotSupportedException("Interface discovery is not supported on NetWasm.");
    }
    public virtual Type[] GetInterfaces() =>
        throw new PlatformNotSupportedException("Interface discovery is not supported on NetWasm.");
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

    public virtual Type? GetNestedType(string name, BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

    public Type[] GetNestedTypes() => GetNestedTypes(DefaultLookup);

    public virtual Type[] GetNestedTypes(BindingFlags bindingAttr) => throw UnsupportedMemberLookup();

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

    private const BindingFlags DefaultLookup = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

    private static PlatformNotSupportedException UnsupportedMemberLookup() => new(
        "Runtime member lookup is unavailable because reflection metadata is not deployed.");
}
