// Portions adapted from dotnet/runtime System.Private.CoreLib GC contracts at
// commit 474ed963699fc29809015392738a15231aaae2de.
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
        var size = GCMetricRuntime.Read(LiveHeapSizeMetric);

        if (forceFullCollection)
        {
            var repetitions = 20;
            long difference;

            do
            {
                WaitForPendingFinalizers();
                Collect();

                var newSize = GCMetricRuntime.Read(LiveHeapSizeMetric);
                difference = (newSize - size) * 100 / size;
                size = newSize;
            }
            while (repetitions-- > 0 && !(-5 < difference && difference < 5));
        }

        return size;
    }

    /// <summary>Gets the total bytes allocated by the managed collector.</summary>
    public static long GetTotalAllocatedBytes(bool precise = false) =>
        GCMetricRuntime.Read(TotalAllocatedBytesMetric);

    /// <summary>Allocates an array.</summary>
    // No collector moves objects, so every array is already pinned.
    public static T[] AllocateArray<T>(int length, bool pinned = false) => new T[length];

    /// <summary>Allocates an array while skipping zero-initialization when possible.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe T[] AllocateUninitializedArray<T>(int length, bool pinned = false)
    {
        // No collector moves objects, so every array is already pinned. The
        // compiler/runtime currently provide ordinary zero-initialized newarr storage.
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
    public static int GetGeneration(WeakReference wo)
    {
        var target = WeakReferenceRuntime.Get(wo.WeakHandle);
        KeepAlive(wo);

        ArgumentNullException.ThrowIfNull(target, nameof(wo));

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

        throw new PlatformNotSupportedException(
            "Completed-collection memory snapshots are not available in the NetWasm collector.");
    }

    public static void AddMemoryPressure(long bytesAllocated)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesAllocated);
        if (IntPtr.Size == 4)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bytesAllocated, int.MaxValue);
        }
        MemoryPressure.Add((ulong)bytesAllocated);
    }

    public static void RemoveMemoryPressure(long bytesAllocated)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesAllocated);
        if (IntPtr.Size == 4)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bytesAllocated, int.MaxValue);
        }
        MemoryPressure.Remove((ulong)bytesAllocated);
    }

    // Mono's memory-pressure heuristic (sgen-gc.c, sgen_add_memory_pressure): pressure added since
    // the last collection runs one once it passes a budget of at least 4 MB, which grows with how
    // little earlier pressure was removed and with a third of the heap. As in Mono, the check reads
    // the slot's total before the addition. Every NetWasm collection is a full one, and NetWasm keeps
    // no collection durations, so Mono's limit on the share of time spent collecting does not apply.
    // The state lives here so that only a program that reports pressure initializes it.
    private static class MemoryPressure
    {
        private const int Count = 4;
        private const ulong MaxRatio = 10;
        private const ulong MinBudget = 4 * 1024 * 1024;
        private static readonly ulong[] s_adds = new ulong[Count];
        private static readonly ulong[] s_removes = new ulong[Count];
        private static long s_collectionCount;
        private static long s_iteration;

        public static void Add(ulong bytesAllocated)
        {
            CheckCounts();
            var slot = (int)(s_iteration % Count);
            var previous = s_adds[slot];
            s_adds[slot] = previous + bytesAllocated;
            if (ShouldCollect(previous))
            {
                GCCollectionRuntime.Collect();
                CheckCounts();
            }
        }

        public static void Remove(ulong bytesRemoved)
        {
            CheckCounts();
            s_removes[(int)(s_iteration % Count)] += bytesRemoved;
        }

        // Starts a new accounting slot after each collection.
        private static void CheckCounts()
        {
            var collections = GCMetricRuntime.Read(CollectionCountMetric);
            if (s_collectionCount != collections)
            {
                s_collectionCount = collections;
                s_iteration++;
                var slot = (int)(s_iteration % Count);
                s_adds[slot] = 0;
                s_removes[slot] = 0;
            }
        }

        private static bool ShouldCollect(ulong newMemoryValue)
        {
            if (newMemoryValue < MinBudget)
            {
                return false;
            }

            ulong added = 0;
            ulong removed = 0;
            for (var index = 0; index < Count; index++)
            {
                added += s_adds[index];
                removed += s_removes[index];
            }
            var slot = (int)(s_iteration % Count);
            added -= s_adds[slot];
            removed -= s_removes[slot];

            var budget = MinBudget;
            if (s_iteration >= Count)
            {
                // The budget scales with the ratio of earlier additions to removals, in fixed point.
                if (added >= removed * MaxRatio)
                {
                    budget = MinBudget * MaxRatio;
                }
                else if (added > removed)
                {
                    budget = added * 1024 / removed * budget / 1024;
                }
            }

            if (newMemoryValue < budget)
            {
                return false;
            }
            var heapOverThree = (ulong)GCMetricRuntime.Read(HeapSizeMetric) / 3;
            if (budget < heapOverThree)
            {
                budget = heapOverThree;
            }
            return newMemoryValue >= budget;
        }
    }

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

    // As upstream, any non-negative generation is valid; every NetWasm collection covers
    // every generation, so an older one runs and counts the ordinary collection.
    private static void ValidateGeneration(int generation) =>
        ArgumentOutOfRangeException.ThrowIfNegative(generation);

    private static void ValidateCollectionMode(GCCollectionMode mode)
    {
        if (mode is < GCCollectionMode.Default or > GCCollectionMode.Aggressive)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
