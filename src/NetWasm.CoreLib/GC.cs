// Portions adapted from dotnet/runtime System.Private.CoreLib GC contracts at
// commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The upstream implementation is licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace System;

/// <summary>Specifies the behavior for a forced garbage collection.</summary>
public enum GCCollectionMode
{
    Default = 0,
    Forced = 1,
    Optimized = 2,
    Aggressive = 3,
}

/// <summary>Describes the result of waiting for a full GC notification.</summary>
public enum GCNotificationStatus
{
    Succeeded = 0,
    Failed = 1,
    Canceled = 2,
    Timeout = 3,
    NotApplicable = 4,
}

public static partial class GC
{
    public static void Collect() => GCCollectionRuntime.Collect();

    public static void Collect(int generation)
    {
        ValidateGeneration(generation);
        GCCollectionRuntime.Collect();
    }

    public static void Collect(int generation, GCCollectionMode mode) =>
        Collect(generation, mode, blocking: true, compacting: false);

    public static void Collect(int generation, GCCollectionMode mode, bool blocking) =>
        Collect(generation, mode, blocking, compacting: false);

    public static void Collect(
        int generation,
        GCCollectionMode mode,
        bool blocking,
        bool compacting)
    {
        ValidateGeneration(generation);
        ValidateCollectionMode(mode);

        if (!blocking)
        {
            throw new PlatformNotSupportedException("Nonblocking collection is not supported.");
        }

        if (compacting || mode == GCCollectionMode.Aggressive)
        {
            throw new PlatformNotSupportedException("Compacting collection is not supported.");
        }

        GCCollectionRuntime.Collect();
    }

    private const int CollectionCountMetric = 0;
    private const int TotalAllocatedBytesMetric = 1;
    private const int HeapSizeMetric = 2;
    private const int LiveHeapSizeMetric = 3;
    private const int TotalPauseMillisecondsMetric = 4;
    private const int PinnedReferenceCountMetric = 5;

    /// <summary>Gets the highest generation supported by the collector.</summary>
    public static int MaxGeneration => 0;

    /// <summary>Returns the number of collections for the supported generation.</summary>
    public static int CollectionCount(int generation)
    {
        ValidateGeneration(generation);
        return (int)GCMetricRuntime.Read(CollectionCountMetric);
    }

    /// <summary>Gets the total bytes currently used by live managed objects.</summary>
    public static long GetTotalMemory(bool forceFullCollection)
    {
        if (forceFullCollection)
        {
            WaitForPendingFinalizers();
            Collect();
        }

        return GCMetricRuntime.Read(LiveHeapSizeMetric);
    }

    /// <summary>Gets the total bytes allocated by the managed collector.</summary>
    public static long GetTotalAllocatedBytes(bool precise = false) =>
        GCMetricRuntime.Read(TotalAllocatedBytesMetric);

    /// <summary>Allocates an array.</summary>
    public static T[] AllocateArray<T>(int length, bool pinned = false)
    {
        if (pinned)
        {
            throw new PlatformNotSupportedException(
                "Pinned array allocation is not supported in the NetWasm collector.");
        }

        return new T[length];
    }

    /// <summary>Allocates an array while skipping zero-initialization when possible.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe T[] AllocateUninitializedArray<T>(int length, bool pinned = false)
    {
        if (pinned)
        {
            throw new PlatformNotSupportedException(
                "Pinned array allocation is not supported in the NetWasm collector.");
        }

        // The compiler/runtime currently provide ordinary zero-initialized newarr storage.
        // That is a permitted implementation of the uninitialized-array contract.
        return new T[length];
    }

    /// <summary>Gets the cumulative time spent in collector pauses.</summary>
    public static TimeSpan GetTotalPauseDuration()
    {
        if (!GCMetricSupportRuntime.IsSupported(TotalPauseMillisecondsMetric))
        {
            throw new PlatformNotSupportedException(
                "Collector pause-time measurement is not available in the NetWasm runtime.");
        }

        return TimeSpan.FromMilliseconds(GCMetricRuntime.Read(TotalPauseMillisecondsMetric));
    }

    /// <summary>Returns the generation of an object in the single-generation collector.</summary>
    public static int GetGeneration(object? value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return 0;
    }

    /// <summary>Returns the generation of a weak-reference wrapper.</summary>
    public static int GetGeneration(WeakReference value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var target = value.Target;
        KeepAlive(value);
        if (target is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return GetGeneration(target);
    }

    /// <summary>Waits until pending managed finalizers have run.</summary>
    public static void WaitForPendingFinalizers() => GCFinalizerRuntime.WaitForPending();

    /// <summary>Preserves an object reference through the call site.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void KeepAlive(object? value)
    {
        // The call itself is the liveness boundary. There is no additional
        // runtime work required by the single-threaded collector.
    }

    /// <summary>Gets the latest collector memory snapshot.</summary>
    public static GCMemoryInfo GetGCMemoryInfo() => GetGCMemoryInfo(GCKind.Any);

    /// <summary>Gets the latest collector memory snapshot for a collection kind.</summary>
    public static GCMemoryInfo GetGCMemoryInfo(GCKind kind)
    {
        if ((uint)kind > (uint)GCKind.Background)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        // Boehm exposes one collection kind only. Returning a synthetic
        // ephemeral/full/background snapshot would claim information that the
        // collector does not provide.
        if (kind != GCKind.Any)
        {
            throw new PlatformNotSupportedException(
                "Collection-kind snapshots are not available in the NetWasm collector.");
        }

        var heapSize = GCMetricRuntime.Read(HeapSizeMetric);
        var liveHeapSize = GCMetricRuntime.Read(LiveHeapSizeMetric);
        var fragmentedBytes = heapSize >= liveHeapSize ? heapSize - liveHeapSize : 0;
        return new GCMemoryInfo(
            heapSize,
            fragmentedBytes,
            GCMetricRuntime.Read(CollectionCountMetric),
            GCMetricRuntime.Read(PinnedReferenceCountMetric));
    }

    public static void AddMemoryPressure(long bytesAllocated) =>
        throw new PlatformNotSupportedException(
            "GC memory-pressure accounting is not available in the NetWasm collector.");

    public static void RemoveMemoryPressure(long bytesAllocated) =>
        throw new PlatformNotSupportedException(
            "GC memory-pressure accounting is not available in the NetWasm collector.");

    public static long GetAllocatedBytesForCurrentThread() =>
        throw new PlatformNotSupportedException(
            "Per-thread allocation accounting is not available in the NetWasm collector.");

    public static Collections.Generic.IReadOnlyDictionary<string, object> GetConfigurationVariables() =>
        throw new PlatformNotSupportedException(
            "Collector configuration variables are not available in the NetWasm runtime.");

    public static void RefreshMemoryLimit() =>
        throw new PlatformNotSupportedException(
            "Refreshing the collector memory limit is not available in the NetWasm runtime.");

    public static void RegisterForFullGCNotification(int maxGenerationThreshold, int largeObjectHeapThreshold) =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static void CancelFullGCNotification() =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static GCNotificationStatus WaitForFullGCApproach() =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static GCNotificationStatus WaitForFullGCApproach(int millisecondsTimeout) =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static GCNotificationStatus WaitForFullGCApproach(TimeSpan timeout) =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static GCNotificationStatus WaitForFullGCComplete() =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static GCNotificationStatus WaitForFullGCComplete(int millisecondsTimeout) =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static GCNotificationStatus WaitForFullGCComplete(TimeSpan timeout) =>
        throw new PlatformNotSupportedException(
            "Full-GC notifications are not available in the NetWasm collector.");

    public static bool TryStartNoGCRegion(long totalSize) =>
        throw new PlatformNotSupportedException(
            "No-GC regions are not available in the NetWasm collector.");

    public static bool TryStartNoGCRegion(long totalSize, long lohSize) =>
        throw new PlatformNotSupportedException(
            "No-GC regions are not available in the NetWasm collector.");

    public static bool TryStartNoGCRegion(long totalSize, bool disallowFullBlockingGC) =>
        throw new PlatformNotSupportedException(
            "No-GC regions are not available in the NetWasm collector.");

    public static bool TryStartNoGCRegion(long totalSize, long lohSize, bool disallowFullBlockingGC) =>
        throw new PlatformNotSupportedException(
            "No-GC regions are not available in the NetWasm collector.");

    public static void EndNoGCRegion() =>
        throw new PlatformNotSupportedException(
            "No-GC regions are not available in the NetWasm collector.");

    public static void RegisterNoGCRegionCallback(long totalSize, Action callback) =>
        throw new PlatformNotSupportedException(
            "No-GC-region callbacks are not available in the NetWasm collector.");

    private static void ValidateGeneration(int generation)
    {
        if (generation != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }
    }

    private static void ValidateCollectionMode(GCCollectionMode mode)
    {
        if (mode is < GCCollectionMode.Default or > GCCollectionMode.Aggressive)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
