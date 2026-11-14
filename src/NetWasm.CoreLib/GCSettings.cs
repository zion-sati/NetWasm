// Portions adapted from dotnet/runtime System.Private.CoreLib GCSettings.cs at
// commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
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
