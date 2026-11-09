// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Upstream base/signature contracts; deployed runtime assembly metadata is unavailable.
using System.Runtime.Serialization;
namespace System.Reflection;

public abstract partial class Assembly : ICustomAttributeProvider, ISerializable
{
    protected Assembly() { }
    public virtual ManifestResourceInfo? GetManifestResourceInfo(string resourceName) => throw new PlatformNotSupportedException("Runtime assembly resource discovery is not supported on NetWasm.");
    public override bool Equals(object? value) => base.Equals(value);
    public override int GetHashCode() => base.GetHashCode();
    public static bool operator ==(Assembly? left, Assembly? right)
    {
        if (right is null) return left is null;
        if (ReferenceEquals(left, right)) return true;
        return left is not null && left.Equals(right);
    }
    public static bool operator !=(Assembly? left, Assembly? right) => !(left == right);
    public static Assembly Load(AssemblyName assemblyRef) => throw new PlatformNotSupportedException("Runtime assembly loading is not supported on NetWasm.");
    public virtual object[] GetCustomAttributes(bool inherit) => throw new PlatformNotSupportedException("Runtime assembly attributes are not available on NetWasm.");
    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) => throw new PlatformNotSupportedException("Runtime assembly attributes are not available on NetWasm.");
    public virtual bool IsDefined(Type attributeType, bool inherit) => throw new PlatformNotSupportedException("Runtime assembly attributes are not available on NetWasm.");
    public virtual void GetObjectData(SerializationInfo info, StreamingContext context) => throw new PlatformNotSupportedException("Assembly serialization is not supported on NetWasm.");
    public static Assembly Load(byte[] rawAssembly) => throw new PlatformNotSupportedException("Runtime assembly loading is not supported on NetWasm.");
    public static Assembly Load(byte[] rawAssembly, byte[]? rawSymbolStore) => throw new PlatformNotSupportedException("Runtime assembly loading is not supported on NetWasm.");
    public static Assembly Load(string assemblyString) => throw new PlatformNotSupportedException("Runtime assembly loading is not supported on NetWasm.");
}
