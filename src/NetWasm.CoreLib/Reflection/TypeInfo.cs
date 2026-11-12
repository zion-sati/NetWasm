// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection;

public interface IReflectableType
{
    TypeInfo GetTypeInfo();
}

// Upstream's TypeInfo: each member forwards to Type's, which answers it or throws. Every Type object
// is a RuntimeType, a TypeInfo, so GetTypeInfo returns the type itself.
public abstract class TypeInfo : Type, IReflectableType
{
    private const BindingFlags DeclaredOnlyLookup = DefaultLookup | BindingFlags.DeclaredOnly;

    protected TypeInfo() { }
    TypeInfo IReflectableType.GetTypeInfo() => this;
    public virtual Type AsType() => this;
    public virtual Type[] GenericTypeParameters => IsGenericTypeDefinition ? GetGenericArguments() : EmptyTypes;
    public virtual EventInfo? GetDeclaredEvent(string name) => GetEvent(name, DeclaredOnlyLookup);
    public virtual FieldInfo? GetDeclaredField(string name) => GetField(name, DeclaredOnlyLookup);
    public virtual MethodInfo? GetDeclaredMethod(string name) => GetMethod(name, DeclaredOnlyLookup);
    public virtual TypeInfo? GetDeclaredNestedType(string name) => GetNestedType(name, DeclaredOnlyLookup)?.GetTypeInfo();
    public virtual PropertyInfo? GetDeclaredProperty(string name) => GetProperty(name, DeclaredOnlyLookup);

    public virtual IEnumerable<MethodInfo> GetDeclaredMethods(string name)
    {
        foreach (MethodInfo method in GetMethods(DeclaredOnlyLookup))
        {
            if (method.Name == name)
                yield return method;
        }
    }

    public virtual IEnumerable<ConstructorInfo> DeclaredConstructors => GetConstructors(DeclaredOnlyLookup);
    public virtual IEnumerable<EventInfo> DeclaredEvents => GetEvents(DeclaredOnlyLookup);
    public virtual IEnumerable<FieldInfo> DeclaredFields => GetFields(DeclaredOnlyLookup);
    public virtual IEnumerable<MemberInfo> DeclaredMembers => GetMembers(DeclaredOnlyLookup);
    public virtual IEnumerable<MethodInfo> DeclaredMethods => GetMethods(DeclaredOnlyLookup);

    public virtual IEnumerable<TypeInfo> DeclaredNestedTypes
    {
        get
        {
            foreach (Type t in GetNestedTypes(DeclaredOnlyLookup))
            {
                yield return t.GetTypeInfo();
            }
        }
    }

    public virtual IEnumerable<PropertyInfo> DeclaredProperties => GetProperties(DeclaredOnlyLookup);
    public virtual IEnumerable<Type> ImplementedInterfaces => GetInterfaces();
    public virtual bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] TypeInfo? typeInfo) => base.IsAssignableFrom(typeInfo);
}

public static class IntrospectionExtensions
{
    public static TypeInfo GetTypeInfo(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type is IReflectableType reflectableType)
            return reflectableType.GetTypeInfo();
        throw new PlatformNotSupportedException("Runtime TypeInfo discovery is unavailable on NetWasm.");
    }
}

public abstract partial class Assembly
{
    public virtual IEnumerable<TypeInfo> DefinedTypes
    {
        get
        {
            Type[] types = GetTypes();
            TypeInfo[] typeinfos = new TypeInfo[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                typeinfos[i] = types[i].GetTypeInfo();
            }
            return typeinfos;
        }
    }
}
