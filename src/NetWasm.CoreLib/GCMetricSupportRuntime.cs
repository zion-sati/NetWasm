using System.Runtime.CompilerServices;

namespace System;

internal static class GCMetricSupportRuntime
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern bool IsSupported(int metric);
}
