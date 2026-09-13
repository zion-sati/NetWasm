using System.Runtime.CompilerServices;

namespace System;

internal static class GCFinalizerRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void WaitForPending();
}
