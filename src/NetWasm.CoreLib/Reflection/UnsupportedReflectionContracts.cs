namespace System.Reflection;

public class AmbiguousMatchException : SystemException
{
    public AmbiguousMatchException() { }

    public AmbiguousMatchException(string? message) : base(message) { }

    public AmbiguousMatchException(string? message, Exception? innerException) :
        base(message, innerException)
    { }
}

public interface ICustomAttributeProvider
{
    object[] GetCustomAttributes(bool inherit);

    object[] GetCustomAttributes(Type attributeType, bool inherit);

    bool IsDefined(Type attributeType, bool inherit);
}

public abstract class MemberInfo : ICustomAttributeProvider
{
    public virtual MemberTypes MemberType => throw Unsupported();

    public virtual System.Collections.Generic.IEnumerable<CustomAttributeData> CustomAttributes =>
        GetCustomAttributesData();

    public virtual System.Collections.Generic.IList<CustomAttributeData> GetCustomAttributesData() =>
        throw new PlatformNotSupportedException("Runtime custom attribute metadata is not deployed on NetWasm.");

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

    public virtual Type? ReflectedType => throw Unsupported();

    public virtual Module Module => throw Unsupported();

    public virtual int MetadataToken => throw Unsupported();

    public virtual bool HasSameMetadataDefinitionAs(MemberInfo other) => throw Unsupported();

    public virtual object[] GetCustomAttributes(bool inherit) =>
        throw Unsupported();

    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) =>
        throw Unsupported();

    public virtual bool IsDefined(Type attributeType, bool inherit) =>
        throw Unsupported();

    private static PlatformNotSupportedException Unsupported() => new(
        "Runtime member metadata is unavailable because reflection metadata is not deployed.");
}

public abstract partial class MethodBase : MemberInfo
{
    public virtual bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

    public virtual bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

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

public abstract partial class MethodInfo : MethodBase
{
    // Equality declarations retained from upstream MethodInfo for standard DLL binding.
    public override bool Equals(object? obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();

    public static bool operator ==(MethodInfo? left, MethodInfo? right)
    {
        // Test "right" first to allow branch elimination when inlined for null checks (== null)
        // so it can become a simple test
        if (right is null)
        {
            return left is null;
        }

        // Try fast reference equality and opposite null check prior to calling the slower virtual Equals
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null && left.Equals(right);
    }

    public static bool operator !=(MethodInfo? left, MethodInfo? right) => !(left == right);

    public virtual Type ReturnType => throw UnsupportedMetadata();

    public override bool IsGenericMethod => false;

    public override bool IsGenericMethodDefinition => false;

    public override bool ContainsGenericParameters => false;

    internal virtual PropertyInfo? AssociatedProperty => null;

    internal virtual bool IsExpressionExecutable => false;

    private static PlatformNotSupportedException UnsupportedMetadata() => new(
        "Runtime method metadata is unavailable because its compiler descriptor was not deployed.");
}

public abstract partial class ConstructorInfo : MethodBase;

public abstract partial class FieldInfo : MemberInfo
{
    public virtual Type FieldType => throw UnsupportedMetadata();

    public virtual bool IsStatic => (Attributes & FieldAttributes.Static) != 0;

    public virtual bool IsInitOnly => (Attributes & FieldAttributes.InitOnly) != 0;

    public virtual bool IsLiteral => (Attributes & FieldAttributes.Literal) != 0;

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

public abstract partial class PropertyInfo : MemberInfo
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

public partial class ParameterInfo
{
    protected ParameterInfo() { }
    public virtual System.Collections.Generic.IEnumerable<CustomAttributeData> CustomAttributes =>
        GetCustomAttributesData();

    public virtual System.Collections.Generic.IList<CustomAttributeData> GetCustomAttributesData() =>
        throw new PlatformNotSupportedException("Runtime custom attribute metadata is not deployed on NetWasm.");

    public virtual Type ParameterType => ClassImpl!;
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
    public override CallingConventions CallingConvention => throw new PlatformNotSupportedException(
        "Runtime method calling-convention metadata is not recorded by this NetWasm descriptor.");

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
    public override Type[] GetOptionalCustomModifiers() => throw MissingModifiers();
    public override Type[] GetRequiredCustomModifiers() => throw MissingModifiers();
    private static PlatformNotSupportedException MissingModifiers() => new(
        "Runtime property custom-modifier metadata is not recorded by this NetWasm descriptor.");

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
    public override ParameterAttributes Attributes => throw MissingMetadata();
    public override MemberInfo Member => throw MissingMetadata();
    public override string? Name => throw MissingMetadata();
    public override int Position => throw MissingMetadata();
    public override int MetadataToken => throw MissingMetadata();
    public override bool IsDefined(Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        throw MissingMetadata();
    }
    public override object[] GetCustomAttributes(bool inherit) => throw MissingMetadata();
    public override object[] GetCustomAttributes(Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        throw MissingMetadata();
    }
    public override Type[] GetOptionalCustomModifiers() => throw MissingMetadata();
    public override Type[] GetRequiredCustomModifiers() => throw MissingMetadata();

    private static PlatformNotSupportedException MissingMetadata() => new(
        "Requested runtime parameter metadata is not recorded by this NetWasm descriptor.");
}
