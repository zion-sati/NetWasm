using System.Reflection;

namespace System.Runtime.CompilerServices
{
    // A delegate type's Invoke method for a type known when compiling. The compiler replaces
    // InvokeMethodHandle<TDelegate> with that Invoke's method token, so only the delegate types code
    // names carry Invoke metadata; a delegate known only at run time has none.
    internal static class DelegateMetadata
    {
        internal static MethodInfo GetInvokeMethod<TDelegate>() =>
            (MethodInfo)MethodBase.GetMethodFromHandle(
                InvokeMethodHandle<TDelegate>(),
                typeof(TDelegate).TypeHandle)!;

        // Delegate.DynamicInvoke on a delegate whose type the call site names; the compiler calls
        // this in its place.
        internal static object? DynamicInvoke<TDelegate>(Delegate target, object?[]? args) =>
            RuntimeMemberExecution.InvokeDelegate(
                GetInvokeMethod<TDelegate>(),
                target ?? throw new NullReferenceException(),
                args);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern RuntimeMethodHandle InvokeMethodHandle<TDelegate>();

        // What InvokeMethodHandle becomes for a type argument that is not a concrete delegate type.
        internal static RuntimeMethodHandle ThrowNotDelegate() =>
            throw new ArgumentException("The type must be a concrete delegate type.");

        internal static PlatformNotSupportedException UnknownDelegateType() => new(
            "A delegate's Invoke metadata is available only for a delegate type known when compiling, " +
            "such as a variable of that delegate type; this delegate's type is known only at run time.");
    }
}
