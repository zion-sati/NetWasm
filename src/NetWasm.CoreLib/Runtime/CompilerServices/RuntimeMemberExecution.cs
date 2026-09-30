using System.Reflection;

namespace System.Runtime.CompilerServices
{
    internal static class RuntimeMemberExecution
    {
        internal static object? InvokeMethod(
            MethodInfo method,
            object? receiver,
            object?[]? arguments) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower bounded method execution.");

        internal static object? ReadField(FieldInfo field, object? receiver) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower bounded field execution.");

        internal static object? ThrowUnsupported() =>
            throw new NotSupportedException(
                "The selected member is outside the executable expression-tree profile.");
    }
}
