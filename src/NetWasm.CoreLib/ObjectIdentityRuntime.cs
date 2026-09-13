using System.Runtime.CompilerServices;

namespace System;

internal static class ObjectIdentityRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int GetHashCode(object? value);
}
