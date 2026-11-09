// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Partial upstream base contract; native metadata discovery is unavailable.
using System.Runtime.Serialization;
namespace System.Reflection;

public abstract partial class Module : ICustomAttributeProvider, ISerializable
{
    protected Module() { }
    public ModuleHandle ModuleHandle => GetModuleHandleImpl();
    private protected virtual ModuleHandle GetModuleHandleImpl() => ModuleHandle.EmptyHandle;
    public virtual Assembly Assembly => throw Unavailable();
    public virtual string FullyQualifiedName => throw Unavailable();
    public virtual string Name => throw Unavailable();
    public virtual string ScopeName => throw Unavailable();
    public virtual int MDStreamVersion => throw Unavailable();
    public virtual Guid ModuleVersionId => throw Unavailable();
    public virtual bool IsResource() => throw Unavailable();
    public virtual Type[] GetTypes() => throw Unavailable();
    public virtual bool IsDefined(Type attributeType, bool inherit) => throw Unavailable();
    public virtual object[] GetCustomAttributes(bool inherit) => throw Unavailable();
    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) => throw Unavailable();
    public virtual void GetObjectData(SerializationInfo info, StreamingContext context) => throw Unavailable();
    public override bool Equals(object? value) => base.Equals(value);
    public override int GetHashCode() => base.GetHashCode();
    public static bool operator ==(Module? left, Module? right)
    {
        if (right is null) return left is null;
        if (ReferenceEquals(left, right)) return true;
        return left is not null && left.Equals(right);
    }
    public static bool operator !=(Module? left, Module? right) => !(left == right);
    public override string ToString() => ScopeName;
    private static PlatformNotSupportedException Unavailable() => new("Runtime module metadata is not available on NetWasm.");
}
