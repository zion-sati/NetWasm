// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection;

public abstract partial class FieldInfo
{
    public virtual RuntimeFieldHandle FieldHandle => throw UnsupportedMetadata();
    public virtual object? GetValue(object? obj) => throw UnsupportedFieldAccess();
    public void SetValue(object? obj, object? value) => throw UnsupportedFieldAccess();
    public virtual void SetValue(object? obj, object? value, BindingFlags invokeAttr,
        Binder? binder, System.Globalization.CultureInfo? culture) => throw UnsupportedFieldAccess();
    // NetWasm: field access through a TypedReference is unavailable like the other runtime field access, so
    // these throw PlatformNotSupportedException where upstream's base throws NotSupportedException.
    [CLSCompliant(false)]
    public virtual void SetValueDirect(TypedReference obj, object value) => throw UnsupportedFieldAccess();
    [CLSCompliant(false)]
    public virtual object? GetValueDirect(TypedReference obj) => throw UnsupportedFieldAccess();
    public virtual object? GetRawConstantValue() => throw UnsupportedMetadata();
    public virtual Type[] GetOptionalCustomModifiers() => throw UnsupportedMetadata();
    public virtual Type[] GetRequiredCustomModifiers() => throw UnsupportedMetadata();
    private static PlatformNotSupportedException UnsupportedFieldAccess() => new(
        "Runtime reflection field access is unavailable on NetWasm; supported expression-tree access is a separate contract.");

    public override MemberTypes MemberType => MemberTypes.Field;
    public virtual FieldAttributes Attributes => throw UnsupportedMetadata();
    public bool IsNotSerialized => (Attributes & FieldAttributes.NotSerialized) != 0;
    public bool IsPinvokeImpl => (Attributes & FieldAttributes.PinvokeImpl) != 0;
    public bool IsSpecialName => (Attributes & FieldAttributes.SpecialName) != 0;
    public bool IsAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Assembly;
    public bool IsFamily => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Family;
    public bool IsFamilyAndAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.FamANDAssem;
    public bool IsFamilyOrAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.FamORAssem;
    public bool IsPrivate => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private;
    public bool IsPublic => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
    public virtual bool IsSecurityCritical => true;
    public virtual bool IsSecuritySafeCritical => false;
    public virtual bool IsSecurityTransparent => false;
    public override bool Equals(object? obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();
    public static bool operator ==(FieldInfo? left, FieldInfo? right)
    {
        if (right is null) return left is null;
        if (ReferenceEquals(left, right)) return true;
        return left is not null && left.Equals(right);
    }
    public static bool operator !=(FieldInfo? left, FieldInfo? right) => !(left == right);
}
