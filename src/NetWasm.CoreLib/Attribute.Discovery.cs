// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace System;

[System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public abstract partial class Attribute
{
    // Assembly and Module as upstream (Attribute.CoreCLR.cs): each validates its arguments, then asks the element,
    // whose own methods throw PlatformNotSupportedException where a query was not resolved at compile time.
    #region Module
    public static Attribute[] GetCustomAttributes(Module element, Type attributeType)
    {
        return GetCustomAttributes(element, attributeType, true);
    }

    public static Attribute[] GetCustomAttributes(Module element)
    {
        return GetCustomAttributes(element, true);
    }

    public static Attribute[] GetCustomAttributes(Module element, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (Attribute[])element.GetCustomAttributes(typeof(Attribute), inherit);
    }

    public static Attribute[] GetCustomAttributes(Module element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);

        if (!attributeType.IsSubclassOf(typeof(Attribute)) && attributeType != typeof(Attribute))
            throw new ArgumentException(SR.Argument_MustHaveAttributeBaseClass);

        return (Attribute[])element.GetCustomAttributes(attributeType, inherit);
    }

    public static bool IsDefined(Module element, Type attributeType)
    {
        return IsDefined(element, attributeType, false);
    }

    public static bool IsDefined(Module element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);

        // Returns true is a custom attribute subclass of attributeType class/interface with no inheritance walk

        if (!attributeType.IsSubclassOf(typeof(Attribute)) && attributeType != typeof(Attribute))
            throw new ArgumentException(SR.Argument_MustHaveAttributeBaseClass);

        return element.IsDefined(attributeType, false);
    }

    public static Attribute? GetCustomAttribute(Module element, Type attributeType)
    {
        return GetCustomAttribute(element, attributeType, true);
    }

    public static Attribute? GetCustomAttribute(Module element, Type attributeType, bool inherit)
    {
        // Returns an Attribute of base class/interface attributeType on the Module or null if none exists.
        // throws an AmbiguousMatchException if there are more than one defined.
        Attribute[] attrib = GetCustomAttributes(element, attributeType, inherit);

        if (attrib == null || attrib.Length == 0)
            return null;

        Attribute match = attrib[0];
        if (attrib.Length == 1)
            return match;

        throw ThrowHelper.GetAmbiguousMatchException(match);
    }

    #endregion

    #region Assembly
    public static Attribute[] GetCustomAttributes(Assembly element, Type attributeType)
    {
        return GetCustomAttributes(element, attributeType, true);
    }

    public static Attribute[] GetCustomAttributes(Assembly element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);

        if (!attributeType.IsSubclassOf(typeof(Attribute)) && attributeType != typeof(Attribute))
            throw new ArgumentException(SR.Argument_MustHaveAttributeBaseClass);

        return (Attribute[])element.GetCustomAttributes(attributeType, inherit);
    }

    public static Attribute[] GetCustomAttributes(Assembly element)
    {
        return GetCustomAttributes(element, true);
    }

    public static Attribute[] GetCustomAttributes(Assembly element, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (Attribute[])element.GetCustomAttributes(typeof(Attribute), inherit);
    }

    public static bool IsDefined(Assembly element, Type attributeType)
    {
        return IsDefined(element, attributeType, true);
    }

    public static bool IsDefined(Assembly element, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(attributeType);

        // Returns true is a custom attribute subclass of attributeType class/interface with no inheritance walk

        if (!attributeType.IsSubclassOf(typeof(Attribute)) && attributeType != typeof(Attribute))
            throw new ArgumentException(SR.Argument_MustHaveAttributeBaseClass);

        return element.IsDefined(attributeType, false);
    }

    public static Attribute? GetCustomAttribute(Assembly element, Type attributeType)
    {
        return GetCustomAttribute(element, attributeType, true);
    }

    public static Attribute? GetCustomAttribute(Assembly element, Type attributeType, bool inherit)
    {
        // Returns an Attribute of base class/interface attributeType on the Assembly or null if none exists.
        // throws an AmbiguousMatchException if there are more than one defined.
        Attribute[] attrib = GetCustomAttributes(element, attributeType, inherit);

        if (attrib == null || attrib.Length == 0)
            return null;

        Attribute match = attrib[0];
        if (attrib.Length == 1)
            return match;

        throw ThrowHelper.GetAmbiguousMatchException(match);
    }

    #endregion

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
