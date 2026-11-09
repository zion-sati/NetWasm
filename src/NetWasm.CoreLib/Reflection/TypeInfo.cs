// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Reflection;

public interface IReflectableType
{
    TypeInfo GetTypeInfo();
}

// Preserve the upstream hierarchy and value forwarding. Runtime discovery is
// unavailable; custom metadata construction is rejected by the protected Type ctor.
public abstract class TypeInfo : Type, IReflectableType
{
    protected TypeInfo() { }
    TypeInfo IReflectableType.GetTypeInfo() => this;
    public virtual Type AsType() => this;
    public virtual Type[] GenericTypeParameters => IsGenericTypeDefinition ? GetGenericArguments() : EmptyTypes;
    public virtual EventInfo? GetDeclaredEvent(string name) => throw Unsupported();
    public virtual FieldInfo? GetDeclaredField(string name) => throw Unsupported();
    public virtual MethodInfo? GetDeclaredMethod(string name) => throw Unsupported();
    public virtual TypeInfo? GetDeclaredNestedType(string name) => throw Unsupported();
    public virtual PropertyInfo? GetDeclaredProperty(string name) => throw Unsupported();
    public virtual IEnumerable<MethodInfo> GetDeclaredMethods(string name) => throw Unsupported();
    public virtual IEnumerable<ConstructorInfo> DeclaredConstructors => throw Unsupported();
    public virtual IEnumerable<EventInfo> DeclaredEvents => throw Unsupported();
    public virtual IEnumerable<FieldInfo> DeclaredFields => throw Unsupported();
    public virtual IEnumerable<MemberInfo> DeclaredMembers => throw Unsupported();
    public virtual IEnumerable<MethodInfo> DeclaredMethods => throw Unsupported();
    public virtual IEnumerable<TypeInfo> DeclaredNestedTypes => throw Unsupported();
    public virtual IEnumerable<PropertyInfo> DeclaredProperties => throw Unsupported();
    public virtual IEnumerable<Type> ImplementedInterfaces => throw Unsupported();
    public virtual bool IsAssignableFrom(TypeInfo? typeInfo) => base.IsAssignableFrom(typeInfo);
    private static PlatformNotSupportedException Unsupported() => new("Runtime type introspection is unavailable on NetWasm.");
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
    public virtual IEnumerable<TypeInfo> DefinedTypes =>
        throw new PlatformNotSupportedException("Runtime assembly type introspection is unavailable on NetWasm.");
}
