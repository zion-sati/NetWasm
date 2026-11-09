// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection;

public abstract partial class MethodInfo
{
    public override MemberTypes MemberType => MemberTypes.Method;
    public virtual ParameterInfo ReturnParameter => throw UnsupportedMetadata();
    public override Type[] GetGenericArguments() => throw UnsupportedMetadata();
    public virtual MethodInfo GetGenericMethodDefinition() => throw UnsupportedMetadata();
    public virtual MethodInfo MakeGenericMethod(params Type[] typeArguments) => throw UnsupportedMetadata();
    public virtual MethodInfo GetBaseDefinition() => throw UnsupportedMetadata();
    public virtual ICustomAttributeProvider ReturnTypeCustomAttributes => throw UnsupportedMetadata();
    public virtual Delegate CreateDelegate(Type delegateType) => throw UnsupportedDelegateCreation();
    public virtual Delegate CreateDelegate(Type delegateType, object? target) => throw UnsupportedDelegateCreation();
    private static PlatformNotSupportedException UnsupportedDelegateCreation() => new(
        "Runtime reflection delegate creation is unavailable on NetWasm; ordinary compiled delegates remain supported.");
}
