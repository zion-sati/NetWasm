using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices;

internal static class ConditionalWeakTableRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int Create(object key, object? value);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern object? GetKey(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern object? GetValue(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Release(int handle);
}
