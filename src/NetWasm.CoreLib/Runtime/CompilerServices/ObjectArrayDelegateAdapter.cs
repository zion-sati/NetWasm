namespace System.Runtime.CompilerServices
{
    internal abstract class ObjectArrayDelegateTarget
    {
        internal abstract object? InvokeCore(object?[] arguments);

        internal TResult Invoke1<T0, TResult>(T0 argument)
        {
            object? result = InvokeCore(new object?[] { argument });
            return (TResult)result!;
        }
    }

    internal static class ObjectArrayDelegateAdapter
    {
        internal static TDelegate Create<TDelegate>(ObjectArrayDelegateTarget target) =>
            throw new InvalidOperationException(
                "The NetWasm compiler did not lower the object-array delegate adapter.");

        internal static object ThrowUnsupported() =>
            throw new NotSupportedException(
                "Only unary delegates with a non-by-reference result are currently executable.");

        internal static object ThrowInvalidDelegate() =>
            throw new ArgumentException("The lambda type must be a delegate type.");
    }
}
