// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection;

// Upstream managed descriptor storage. RuntimeParameterInfo preserves its narrower
// compiler-generated type descriptor and rejects metadata it does not record.
#pragma warning disable SYSLIB0050 // Standard compatibility includes the legacy formatter interface.
public partial class ParameterInfo : ICustomAttributeProvider, System.Runtime.Serialization.IObjectReference
#pragma warning restore SYSLIB0050
{
    // Upstream formatting needs root-element and nested-type metadata not deployed here.
    public override string ToString() => throw new PlatformNotSupportedException(
        "Formatting parameter metadata requires unavailable type-formatting metadata on NetWasm.");

    public virtual object? DefaultValue => throw UnavailableMetadata();
    public virtual object? RawDefaultValue => throw UnavailableMetadata();
    public virtual bool HasDefaultValue => throw UnavailableMetadata();
    public virtual bool IsDefined(Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        return false;
    }
    public virtual object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();
    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        return Array.Empty<object>();
    }
    public virtual Type[] GetOptionalCustomModifiers() => Type.EmptyTypes;
    public virtual Type[] GetRequiredCustomModifiers() => Type.EmptyTypes;
    public virtual int MetadataToken => 0x08000000;
    public object GetRealObject(System.Runtime.Serialization.StreamingContext context) =>
        throw new PlatformNotSupportedException("Formatter-based parameter metadata reconstruction is unavailable on NetWasm.");
    private static PlatformNotSupportedException UnavailableMetadata() => new(
        "Parameter default-value metadata is unavailable on NetWasm.");

    public virtual ParameterAttributes Attributes => AttrsImpl;
    public virtual MemberInfo Member => MemberImpl;
    public virtual string? Name => NameImpl;
    public virtual int Position => PositionImpl;
    public bool IsIn => (Attributes & ParameterAttributes.In) != 0;
    public bool IsLcid => (Attributes & ParameterAttributes.Lcid) != 0;
    public bool IsOptional => (Attributes & ParameterAttributes.Optional) != 0;
    public bool IsOut => (Attributes & ParameterAttributes.Out) != 0;
    public bool IsRetval => (Attributes & ParameterAttributes.Retval) != 0;
    protected ParameterAttributes AttrsImpl;
    protected Type? ClassImpl;
    protected object? DefaultValueImpl;
    protected MemberInfo MemberImpl = null!;
    protected string? NameImpl;
    protected int PositionImpl;
}
