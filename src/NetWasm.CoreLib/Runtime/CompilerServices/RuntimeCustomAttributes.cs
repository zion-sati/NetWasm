namespace System.Runtime.CompilerServices;

internal static class RuntimeCustomAttributes
{
    internal static void ApplySetter<T>(Action<T> setter, T value, string name)
    {
        try
        {
            setter(value);
        }
        catch (Exception exception)
        {
            throw new Reflection.CustomAttributeFormatException(
                "'" + name + "' property specified was not found.",
                new Reflection.TargetInvocationException(exception));
        }
    }

    internal static Attribute? GetOne(
        Type target,
        Type attributeType,
        bool inherit) => throw Unsupported();

    internal static T? GetOne<T>(Type target, bool inherit)
        where T : Attribute => throw Unsupported();

    internal static Attribute[] GetMany(
        Type target,
        Type attributeType,
        bool inherit) => throw Unsupported();

    internal static T[] GetMany<T>(Type target, bool inherit)
        where T : Attribute => throw Unsupported();

    internal static bool IsDefined(
        Type target,
        Type attributeType,
        bool inherit) => throw Unsupported();

    private static PlatformNotSupportedException Unsupported() => new(
        "Custom attribute queries must be resolved by the NetWasm compiler.");
}
