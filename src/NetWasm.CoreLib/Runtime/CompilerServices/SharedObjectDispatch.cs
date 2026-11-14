using System.Diagnostics;

namespace System.Runtime.CompilerServices
{
    // The virtual calls object declares, each as one shared body. A call site whose receiver can be any of many
    // allocated types calls one of these instead of carrying its own dispatch over all of them (the compiler
    // redirects it). Hidden from stack traces, so an exception from the receiver's override shows the caller.
    internal static class SharedObjectDispatch
    {
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool DispatchEquals(object receiver, object? other) => receiver.Equals(other);

        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int DispatchGetHashCode(object receiver) => receiver.GetHashCode();

        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string? DispatchToString(object receiver) => receiver.ToString();
    }
}
