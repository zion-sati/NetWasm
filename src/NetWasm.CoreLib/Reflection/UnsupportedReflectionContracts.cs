namespace System.Reflection;

public interface ICustomAttributeProvider
{
    object[] GetCustomAttributes(bool inherit);

    object[] GetCustomAttributes(Type attributeType, bool inherit);

    bool IsDefined(Type attributeType, bool inherit);
}

public abstract class MemberInfo : ICustomAttributeProvider
{
    public override bool Equals(object? value) => base.Equals(value);

    public override int GetHashCode() => base.GetHashCode();

    public static bool operator ==(MemberInfo? left, MemberInfo? right)
    {
        if (right is null)
        {
            return left is null;
        }
        return object.ReferenceEquals(left, right) ||
            left is not null && left.Equals(right);
    }

    public static bool operator !=(MemberInfo? left, MemberInfo? right) => !(left == right);

    public virtual string Name => throw Unsupported();

    public virtual Type? DeclaringType => throw Unsupported();

    public virtual object[] GetCustomAttributes(bool inherit) =>
        throw Unsupported();

    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) =>
        throw Unsupported();

    public virtual bool IsDefined(Type attributeType, bool inherit) =>
        throw Unsupported();

    private static PlatformNotSupportedException Unsupported() => new(
        "Runtime member metadata is unavailable because reflection metadata is not deployed.");
}

public abstract class MethodBase : MemberInfo
{
    public virtual bool IsStatic => throw UnsupportedMetadata();

    public virtual bool IsPublic => throw UnsupportedMetadata();

    public virtual ParameterInfo[] GetParameters() => throw UnsupportedMetadata();

    public static MethodBase GetMethodFromHandle(RuntimeMethodHandle handle) =>
        throw Unsupported();

    public static MethodBase GetMethodFromHandle(
        RuntimeMethodHandle handle,
        RuntimeTypeHandle declaringType) => throw Unsupported();

    private static PlatformNotSupportedException Unsupported() => new(
        "Only compiler-emitted closed member handles are supported.");

    private static PlatformNotSupportedException UnsupportedMetadata() => new(
        "Runtime method metadata is unavailable because its compiler descriptor was not deployed.");

    internal static unsafe ParameterInfo[] CreateParameters(nint typeIds, int count)
    {
        var parameters = new ParameterInfo[count];
        var ids = (int*)typeIds;
        for (var index = 0; index < count; index++)
        {
            parameters[index] = new RuntimeParameterInfo(ids[index]);
        }
        return parameters;
    }
}

public abstract class MethodInfo : MethodBase
{
    public virtual Type ReturnType => throw UnsupportedMetadata();

    public virtual bool IsGenericMethod => false;

    public virtual bool IsGenericMethodDefinition => false;

    public virtual bool ContainsGenericParameters => false;

    internal virtual PropertyInfo? AssociatedProperty => null;

    internal virtual bool IsExpressionExecutable => false;

    private static PlatformNotSupportedException UnsupportedMetadata() => new(
        "Runtime method metadata is unavailable because its compiler descriptor was not deployed.");
}

public abstract class ConstructorInfo : MethodBase;

public abstract class FieldInfo : MemberInfo
{
    public virtual Type FieldType => throw UnsupportedMetadata();

    public virtual bool IsStatic => throw UnsupportedMetadata();

    public virtual bool IsInitOnly => throw UnsupportedMetadata();

    public virtual bool IsLiteral => throw UnsupportedMetadata();

    public static FieldInfo GetFieldFromHandle(RuntimeFieldHandle handle) =>
        throw Unsupported();

    public static FieldInfo GetFieldFromHandle(
        RuntimeFieldHandle handle,
        RuntimeTypeHandle declaringType) => throw Unsupported();

    private static PlatformNotSupportedException Unsupported() => new(
        "Only compiler-emitted closed member handles are supported.");

    private static PlatformNotSupportedException UnsupportedMetadata() => new(
        "Runtime field metadata is unavailable because its compiler descriptor was not deployed.");
}

public abstract class PropertyInfo : MemberInfo
{
    public virtual Type PropertyType => throw UnsupportedMetadata();

    public virtual bool CanRead => GetGetMethod(nonPublic: true) is not null;

    public virtual bool CanWrite => GetSetMethod(nonPublic: true) is not null;

    public virtual MethodInfo? GetGetMethod(bool nonPublic) =>
        throw UnsupportedMetadata();

    public virtual MethodInfo? GetSetMethod(bool nonPublic) =>
        throw UnsupportedMetadata();

    private static PlatformNotSupportedException UnsupportedMetadata() => new(
        "Runtime property metadata is unavailable because its compiler descriptor was not deployed.");
}

public abstract class ParameterInfo
{
    public virtual Type ParameterType => throw new PlatformNotSupportedException(
        "Runtime parameter metadata is unavailable because its compiler descriptor was not deployed.");
}

internal static class RuntimeMemberFlags
{
    internal const int Static = 1 << 0;
    internal const int Public = 1 << 1;
    internal const int InitOnly = 1 << 2;
    internal const int Literal = 1 << 3;
    internal const int GenericMethod = 1 << 4;
    internal const int GenericMethodDefinition = 1 << 5;
    internal const int ContainsGenericParameters = 1 << 6;
    internal const int ExpressionExecutable = 1 << 7;
}

internal sealed class RuntimeMethodInfo : MethodInfo
{
    internal int DeclaringTypeId;
    internal int RequiresDeclaringType;
    internal int Flags;
    internal int ReturnTypeId;
    internal nint ParameterTypeIds;
    internal int ParameterCount;
    internal string? MetadataName;
    internal RuntimePropertyInfo? Property;

    internal RuntimeMethodInfo(int declaringTypeId, int requiresDeclaringType)
    {
        DeclaringTypeId = declaringTypeId;
        RequiresDeclaringType = requiresDeclaringType;
    }

    public override string Name => MetadataName ?? throw MissingName();

    public override Type DeclaringType => Type.GetTypeFromSemanticId(DeclaringTypeId);

    public override bool IsStatic => (Flags & RuntimeMemberFlags.Static) != 0;

    public override bool IsPublic => (Flags & RuntimeMemberFlags.Public) != 0;

    public override ParameterInfo[] GetParameters() =>
        CreateParameters(ParameterTypeIds, ParameterCount);

    public override Type ReturnType => Type.GetTypeFromSemanticId(ReturnTypeId);

    public override bool IsGenericMethod =>
        (Flags & RuntimeMemberFlags.GenericMethod) != 0;

    public override bool IsGenericMethodDefinition =>
        (Flags & RuntimeMemberFlags.GenericMethodDefinition) != 0;

    public override bool ContainsGenericParameters =>
        (Flags & RuntimeMemberFlags.ContainsGenericParameters) != 0;

    internal override PropertyInfo? AssociatedProperty => Property;

    internal override bool IsExpressionExecutable =>
        (Flags & RuntimeMemberFlags.ExpressionExecutable) != 0;

    private static InvalidOperationException MissingName() => new(
        "The compiler-generated member descriptor is missing its demanded name.");
}

internal sealed class RuntimeConstructorInfo : ConstructorInfo
{
    internal int DeclaringTypeId;
    internal int RequiresDeclaringType;
    internal int Flags;
    internal nint ParameterTypeIds;
    internal int ParameterCount;
    internal string? MetadataName;

    internal RuntimeConstructorInfo(int declaringTypeId, int requiresDeclaringType)
    {
        DeclaringTypeId = declaringTypeId;
        RequiresDeclaringType = requiresDeclaringType;
    }

    public override string Name => MetadataName ?? throw MissingName();

    public override Type DeclaringType => Type.GetTypeFromSemanticId(DeclaringTypeId);

    public override bool IsStatic => (Flags & RuntimeMemberFlags.Static) != 0;

    public override bool IsPublic => (Flags & RuntimeMemberFlags.Public) != 0;

    public override ParameterInfo[] GetParameters() =>
        CreateParameters(ParameterTypeIds, ParameterCount);

    private static InvalidOperationException MissingName() => new(
        "The compiler-generated member descriptor is missing its demanded name.");
}

internal sealed class RuntimeFieldInfo : FieldInfo
{
    internal int DeclaringTypeId;
    internal int RequiresDeclaringType;
    internal int Flags;
    internal int FieldTypeId;
    internal string? MetadataName;

    internal RuntimeFieldInfo(int declaringTypeId, int requiresDeclaringType)
    {
        DeclaringTypeId = declaringTypeId;
        RequiresDeclaringType = requiresDeclaringType;
    }

    public override string Name => MetadataName ?? throw MissingName();

    public override Type DeclaringType => Type.GetTypeFromSemanticId(DeclaringTypeId);

    public override Type FieldType => Type.GetTypeFromSemanticId(FieldTypeId);

    public override bool IsStatic => (Flags & RuntimeMemberFlags.Static) != 0;

    public override bool IsInitOnly => (Flags & RuntimeMemberFlags.InitOnly) != 0;

    public override bool IsLiteral => (Flags & RuntimeMemberFlags.Literal) != 0;

    private static InvalidOperationException MissingName() => new(
        "The compiler-generated member descriptor is missing its demanded name.");
}

internal sealed class RuntimePropertyInfo : PropertyInfo
{
    internal int DeclaringTypeId;
    internal int PropertyTypeId;
    internal string? MetadataName;
    internal RuntimeMethodInfo? Getter;
    internal RuntimeMethodInfo? Setter;

    internal RuntimePropertyInfo(int declaringTypeId, int propertyTypeId)
    {
        DeclaringTypeId = declaringTypeId;
        PropertyTypeId = propertyTypeId;
    }

    public override string Name => MetadataName ?? throw MissingName();

    public override Type DeclaringType => Type.GetTypeFromSemanticId(DeclaringTypeId);

    public override Type PropertyType => Type.GetTypeFromSemanticId(PropertyTypeId);

    public override MethodInfo? GetGetMethod(bool nonPublic) =>
        nonPublic || Getter?.IsPublic is true ? Getter : null;

    public override MethodInfo? GetSetMethod(bool nonPublic) =>
        nonPublic || Setter?.IsPublic is true ? Setter : null;

    private static InvalidOperationException MissingName() => new(
        "The compiler-generated member descriptor is missing its demanded name.");
}

internal sealed class RuntimeParameterInfo(int parameterTypeId) : ParameterInfo
{
    internal int ParameterTypeId = parameterTypeId;

    public override Type ParameterType => Type.GetTypeFromSemanticId(ParameterTypeId);
}

public abstract class Binder;

public readonly struct ParameterModifier
{
    public ParameterModifier(int parameterCount) =>
        throw new PlatformNotSupportedException(
            "Runtime parameter metadata is unavailable because reflection metadata is not deployed.");
}
