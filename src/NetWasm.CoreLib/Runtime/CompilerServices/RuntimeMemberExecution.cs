using System.Reflection;

namespace System.Runtime.CompilerServices
{
    internal static class RuntimeMemberExecution
    {
        // A method on its receiver (a struct's on the box's contents), or a constructor on a new object or box,
        // which is the result.
        internal static object? InvokeMethod(
            MethodBase method,
            object? receiver,
            object?[]? arguments) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower bounded method execution.");

        internal static object? InvokeDelegate(
            MethodInfo method,
            Delegate receiver,
            object?[]? arguments) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower bounded delegate execution.");

        internal static object? ReadField(FieldInfo field, object? receiver) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower bounded field execution.");

        // Stores the value, a reference or a box of the field's type, in the receiver's field (a struct's in its box)
        // or the static field; the result is null.
        internal static object? WriteField(FieldInfo field, object? receiver, object? value) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower bounded field execution.");

        internal static object? ThrowUnsupported() =>
            throw new NotSupportedException(
                "The selected member is outside the executable expression-tree profile.");

        internal static object? ThrowDynamicInvokeUnsupported() =>
            throw new NotSupportedException(
                "The delegate signature is outside the executable DynamicInvoke profile.");

        internal static object? ThrowDynamicInvokeArgument() =>
            throw new ArgumentException(
                "Object of the supplied type cannot be converted to a delegate parameter type.");

        internal static object? ThrowDynamicInvokeParameterCount() =>
            throw new TargetParameterCountException();

        internal static object? ThrowTargetInvocation(Exception exception) =>
            throw new TargetInvocationException(exception);
    }
}
