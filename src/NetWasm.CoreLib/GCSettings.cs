// Portions adapted from dotnet/runtime System.Private.CoreLib GCSettings.cs at
// commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The upstream implementation is licensed under the MIT license.

namespace System.Runtime;

public enum GCLargeObjectHeapCompactionMode
{
    Default = 1,
    CompactOnce = 2,
}

public enum GCLatencyMode
{
    Batch = 0,
    Interactive = 1,
    LowLatency = 2,
    SustainedLowLatency = 3,
    NoGCRegion = 4,
}

public static class GCSettings
{
    public static bool IsServerGC => false;

    public static GCLargeObjectHeapCompactionMode LargeObjectHeapCompactionMode
    {
        get => throw Unsupported();
        set
        {
            if (value is < GCLargeObjectHeapCompactionMode.Default or > GCLargeObjectHeapCompactionMode.CompactOnce)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            throw Unsupported();
        }
    }

    public static GCLatencyMode LatencyMode
    {
        get => throw Unsupported();
        set
        {
            if (value is < GCLatencyMode.Batch or > GCLatencyMode.SustainedLowLatency)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            throw Unsupported();
        }
    }

    private static PlatformNotSupportedException Unsupported() =>
        new("Collector tuning is not available in the NetWasm runtime.");
}
