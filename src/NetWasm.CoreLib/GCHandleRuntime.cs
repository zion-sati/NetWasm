using System.Runtime.CompilerServices;

namespace System;

internal static class GCHandleRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int Create(object? target, int kind);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern object? Get(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Set(int handle, object? target);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Release(int handle);

    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern nint Address(int handle);
}
