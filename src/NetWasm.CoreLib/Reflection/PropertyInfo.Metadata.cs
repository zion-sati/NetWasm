// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System.Reflection;

public abstract partial class PropertyInfo
{
    public override MemberTypes MemberType => MemberTypes.Property;
    public virtual ParameterInfo[] GetIndexParameters() => throw UnsupportedMetadata();
    public virtual PropertyAttributes Attributes => throw UnsupportedMetadata();
    public bool IsSpecialName => (Attributes & PropertyAttributes.SpecialName) != 0;
    public MethodInfo[] GetAccessors() => GetAccessors(nonPublic: false);
    public virtual MethodInfo[] GetAccessors(bool nonPublic) => throw UnsupportedMetadata();
    public virtual MethodInfo? GetMethod => GetGetMethod(nonPublic: true);
    public MethodInfo? GetGetMethod() => GetGetMethod(nonPublic: false);
    public virtual MethodInfo? SetMethod => GetSetMethod(nonPublic: true);
    public MethodInfo? GetSetMethod() => GetSetMethod(nonPublic: false);
    public virtual Type[] GetOptionalCustomModifiers() => Type.EmptyTypes;
    public virtual Type[] GetRequiredCustomModifiers() => Type.EmptyTypes;
    public object? GetValue(object? obj) => GetValue(obj, index: null);
    public virtual object? GetValue(object? obj, object?[]? index) =>
        GetValue(obj, BindingFlags.Default, binder: null, index: index, culture: null);
    public virtual object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder,
        object?[]? index, CultureInfo? culture) => throw UnsupportedPropertyAccess();
    public virtual object? GetConstantValue() => throw UnsupportedMetadata();
    public virtual object? GetRawConstantValue() => throw UnsupportedMetadata();
    public void SetValue(object? obj, object? value) => SetValue(obj, value, index: null);
    public virtual void SetValue(object? obj, object? value, object?[]? index) =>
        SetValue(obj, value, BindingFlags.Default, binder: null, index: index, culture: null);
    public virtual void SetValue(object? obj, object? value, BindingFlags invokeAttr,
        Binder? binder, object?[]? index, CultureInfo? culture) => throw UnsupportedPropertyAccess();
    public override bool Equals(object? obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();
    public static bool operator ==(PropertyInfo? left, PropertyInfo? right)
    {
        if (right is null) return left is null;
        if (ReferenceEquals(left, right)) return true;
        return left is not null && left.Equals(right);
    }
    public static bool operator !=(PropertyInfo? left, PropertyInfo? right) => !(left == right);
    private static PlatformNotSupportedException UnsupportedPropertyAccess() => new(
        "Runtime reflection property access is unavailable on NetWasm; supported expression-tree access is a separate contract.");
}
