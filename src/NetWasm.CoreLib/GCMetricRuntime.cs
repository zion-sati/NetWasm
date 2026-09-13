using System.Runtime.CompilerServices;

namespace System;

internal static class GCMetricRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern long Read(int metric);
}
