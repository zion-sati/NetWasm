namespace System.Reflection;

public interface ICustomAttributeProvider
{
    object[] GetCustomAttributes(bool inherit);

    object[] GetCustomAttributes(Type attributeType, bool inherit);

    bool IsDefined(Type attributeType, bool inherit);
}

public abstract class MemberInfo : ICustomAttributeProvider
{
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

public abstract class ConstructorInfo : MemberInfo;

public abstract class PropertyInfo : MemberInfo;

public abstract class Binder;

public readonly struct ParameterModifier
{
    public ParameterModifier(int parameterCount) =>
        throw new PlatformNotSupportedException(
            "Runtime parameter metadata is unavailable because reflection metadata is not deployed.");
}
