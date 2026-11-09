// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System.Reflection;

public abstract partial class MethodBase
{
    public virtual MethodBody? GetMethodBody() => throw new PlatformNotSupportedException(
        "Runtime managed method bodies are not deployed on NetWasm.");
    public virtual MethodAttributes Attributes => throw UnsupportedMetadata();
    public virtual MethodImplAttributes MethodImplementationFlags => GetMethodImplementationFlags();
    public virtual MethodImplAttributes GetMethodImplementationFlags() => throw UnsupportedMetadata();
    public virtual CallingConventions CallingConvention => CallingConventions.Standard;
    public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;
    public bool IsConstructor => this is ConstructorInfo && !IsStatic && (Attributes & MethodAttributes.RTSpecialName) == MethodAttributes.RTSpecialName;
    public bool IsFinal => (Attributes & MethodAttributes.Final) != 0;
    public bool IsHideBySig => (Attributes & MethodAttributes.HideBySig) != 0;
    public bool IsSpecialName => (Attributes & MethodAttributes.SpecialName) != 0;
    public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;
    public bool IsAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly;
    public bool IsFamily => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family;
    public bool IsFamilyAndAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamANDAssem;
    public bool IsFamilyOrAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem;
    public bool IsPrivate => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
    public virtual bool IsConstructedGenericMethod => IsGenericMethod && !IsGenericMethodDefinition;
    public virtual bool IsGenericMethod => false;
    public virtual bool IsGenericMethodDefinition => false;
    public virtual bool ContainsGenericParameters => false;
    public virtual Type[] GetGenericArguments() => throw UnsupportedMetadata();
    public object? Invoke(object? obj, object?[]? parameters) => Invoke(obj, BindingFlags.Default, null, parameters, null);
    public virtual object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) =>
        throw new PlatformNotSupportedException("Runtime reflection method invocation is unavailable on NetWasm.");
    public virtual RuntimeMethodHandle MethodHandle => throw UnsupportedMetadata();
    public virtual bool IsSecurityCritical => throw UnsupportedMetadata();
    public virtual bool IsSecuritySafeCritical => throw UnsupportedMetadata();
    public virtual bool IsSecurityTransparent => throw UnsupportedMetadata();
    public static MethodBase? GetCurrentMethod() => throw new PlatformNotSupportedException(
        "Runtime current-method discovery is unavailable on NetWasm.");
    public override bool Equals(object? obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();
    public static bool operator ==(MethodBase? left, MethodBase? right)
    {
        if (right is null) return left is null;
        if (ReferenceEquals(left, right)) return true;
        return left is not null && left.Equals(right);
    }
    public static bool operator !=(MethodBase? left, MethodBase? right) => !(left == right);
}
