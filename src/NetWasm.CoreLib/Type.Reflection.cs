using System.Reflection;

namespace System;

public sealed partial class Type
{
    public ConstructorInfo? GetConstructor(
        BindingFlags bindingAttr,
        Binder? binder,
        Type[] types,
        ParameterModifier[]? modifiers) =>
        throw UnsupportedMemberLookup();

    public PropertyInfo? GetProperty(
        string name,
        BindingFlags bindingAttr,
        Binder? binder,
        Type? returnType,
        Type[] types,
        ParameterModifier[]? modifiers) =>
        throw UnsupportedMemberLookup();

    private static PlatformNotSupportedException UnsupportedMemberLookup() => new(
        "Runtime member lookup is unavailable because reflection metadata is not deployed.");
}
