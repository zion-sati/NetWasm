// Adapted from dotnet/runtime System.Reflection.CustomAttributeExtensions.
// The upstream implementation is licensed under MIT.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace System.Reflection;

public static class CustomAttributeExtensions
{
    public static Attribute? GetCustomAttribute(
        this MemberInfo element,
        Type attributeType) => GetCustomAttribute(element, attributeType, inherit: true);

    public static Attribute? GetCustomAttribute(
        this MemberInfo element,
        Type attributeType,
        bool inherit) => RuntimeCustomAttributes.GetOne(
            TypeTarget(element),
            attributeType,
            inherit);

    public static T? GetCustomAttribute<T>(this MemberInfo element)
        where T : Attribute => GetCustomAttribute<T>(element, inherit: true);

    public static T? GetCustomAttribute<T>(this MemberInfo element, bool inherit)
        where T : Attribute => RuntimeCustomAttributes.GetOne<T>(
            TypeTarget(element),
            inherit);

    public static IEnumerable<Attribute> GetCustomAttributes(this MemberInfo element) =>
        throw UnsupportedUnfiltered();

    public static IEnumerable<Attribute> GetCustomAttributes(
        this MemberInfo element,
        bool inherit) => throw UnsupportedUnfiltered();

    public static IEnumerable<Attribute> GetCustomAttributes(
        this MemberInfo element,
        Type attributeType) => GetCustomAttributesArray(
            element,
            attributeType,
            inherit: true);

    public static IEnumerable<Attribute> GetCustomAttributes(
        this MemberInfo element,
        Type attributeType,
        bool inherit) => RuntimeCustomAttributes.GetMany(
            TypeTarget(element),
            attributeType,
            inherit);

    public static IEnumerable<T> GetCustomAttributes<T>(this MemberInfo element)
        where T : Attribute => GetCustomAttributes<T>(element, inherit: true);

    public static IEnumerable<T> GetCustomAttributes<T>(
        this MemberInfo element,
        bool inherit)
        where T : Attribute => RuntimeCustomAttributes.GetMany<T>(
            TypeTarget(element),
            inherit);

    public static bool IsDefined(this MemberInfo element, Type attributeType) =>
        IsDefined(element, attributeType, inherit: true);

    public static bool IsDefined(
        this MemberInfo element,
        Type attributeType,
        bool inherit) => RuntimeCustomAttributes.IsDefined(
            TypeTarget(element),
            attributeType,
            inherit);

    internal static Attribute[] GetCustomAttributesArray(
        MemberInfo element,
        Type attributeType,
        bool inherit) => RuntimeCustomAttributes.GetMany(
            TypeTarget(element),
            attributeType,
            inherit);

    private static Type TypeTarget(MemberInfo element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element as Type ?? throw new PlatformNotSupportedException(
            "Custom attribute queries are supported only for statically bounded type targets.");
    }

    private static PlatformNotSupportedException UnsupportedUnfiltered() => new(
        "Unfiltered custom attribute enumeration is not supported. Specify one attribute type.");
}
