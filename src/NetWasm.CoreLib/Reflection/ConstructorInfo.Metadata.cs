// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System.Reflection;

public abstract partial class ConstructorInfo
{
    public override MemberTypes MemberType => MemberTypes.Constructor;
    public override Type[] GetGenericArguments() => Array.Empty<Type>();
    public object Invoke(object?[]? parameters) => Invoke(BindingFlags.Default, null, parameters, null);
    public virtual object Invoke(BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) =>
        throw new PlatformNotSupportedException("Runtime reflection constructor invocation is unavailable on NetWasm.");
    public override bool Equals(object? obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();
    public static bool operator ==(ConstructorInfo? left, ConstructorInfo? right)
    {
        if (right is null) return left is null;
        if (ReferenceEquals(left, right)) return true;
        return left is not null && left.Equals(right);
    }
    public static bool operator !=(ConstructorInfo? left, ConstructorInfo? right) => !(left == right);
    public static readonly string ConstructorName = ".ctor";
    public static readonly string TypeConstructorName = ".cctor";
}
