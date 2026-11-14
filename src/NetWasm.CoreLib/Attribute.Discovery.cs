// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System;

[System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public abstract partial class Attribute
{
    // The API contracts are available to portable libraries, but these targets
    // do not carry discoverable custom attribute metadata in this profile.
    public static Attribute? GetCustomAttribute(Assembly element, Type attributeType) =>
        GetCustomAttribute(element, attributeType, inherit: true);

    public static Attribute? GetCustomAttribute(Assembly element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on Assembly is not supported.");
    }

    public static Attribute[] GetCustomAttributes(Assembly element, Type attributeType) =>
        GetCustomAttributes(element, attributeType, inherit: true);

    public static Attribute[] GetCustomAttributes(Assembly element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on Assembly is not supported.");
    }

    public static bool IsDefined(Assembly element, Type attributeType) =>
        IsDefined(element, attributeType, inherit: true);

    public static bool IsDefined(Assembly element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on Assembly is not supported.");
    }

    public static Attribute? GetCustomAttribute(Module element, Type attributeType) =>
        GetCustomAttribute(element, attributeType, inherit: true);

    public static Attribute? GetCustomAttribute(Module element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on Module is not supported.");
    }

    public static Attribute[] GetCustomAttributes(Module element, Type attributeType) =>
        GetCustomAttributes(element, attributeType, inherit: true);

    public static Attribute[] GetCustomAttributes(Module element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on Module is not supported.");
    }

    public static bool IsDefined(Module element, Type attributeType) =>
        IsDefined(element, attributeType, inherit: true);

    public static bool IsDefined(Module element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on Module is not supported.");
    }

    public static Attribute? GetCustomAttribute(ParameterInfo element, Type attributeType) =>
        GetCustomAttribute(element, attributeType, inherit: true);

    public static Attribute? GetCustomAttribute(ParameterInfo element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on ParameterInfo is not supported.");
    }

    public static Attribute[] GetCustomAttributes(ParameterInfo element, Type attributeType) =>
        GetCustomAttributes(element, attributeType, inherit: true);

    public static Attribute[] GetCustomAttributes(ParameterInfo element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on ParameterInfo is not supported.");
    }

    public static bool IsDefined(ParameterInfo element, Type attributeType) =>
        IsDefined(element, attributeType, inherit: true);

    public static bool IsDefined(ParameterInfo element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);
        throw new PlatformNotSupportedException("Custom attribute discovery on ParameterInfo is not supported.");
    }

    public static Attribute[] GetCustomAttributes(Assembly element) =>
        GetCustomAttributes(element, inherit: true);

    public static Attribute[] GetCustomAttributes(Assembly element, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        throw new PlatformNotSupportedException("Unfiltered custom attribute enumeration is not supported.");
    }

    public static Attribute[] GetCustomAttributes(Module element) =>
        GetCustomAttributes(element, inherit: true);

    public static Attribute[] GetCustomAttributes(Module element, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        throw new PlatformNotSupportedException("Unfiltered custom attribute enumeration is not supported.");
    }

    public static Attribute[] GetCustomAttributes(ParameterInfo element) =>
        GetCustomAttributes(element, inherit: true);

    public static Attribute[] GetCustomAttributes(ParameterInfo element, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        throw new PlatformNotSupportedException("Unfiltered custom attribute enumeration is not supported.");
    }

    public static Attribute[] GetCustomAttributes(MemberInfo element) =>
        GetCustomAttributes(element, inherit: true);

    public static Attribute[] GetCustomAttributes(MemberInfo element, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        throw new PlatformNotSupportedException("Unfiltered custom attribute enumeration is not supported.");
    }
}
