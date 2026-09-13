// Portions adapted from dotnet/runtime System.Private.CoreLib GCMemoryInfo.cs
// at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The upstream implementation is licensed under the MIT license.

namespace System;

/// <summary>Describes the size and fragmentation of a collected generation.</summary>
public readonly struct GCGenerationInfo
{
    internal GCGenerationInfo(
        long sizeBeforeBytes,
        long fragmentationBeforeBytes,
        long sizeAfterBytes,
        long fragmentationAfterBytes)
    {
        SizeBeforeBytes = sizeBeforeBytes;
        FragmentationBeforeBytes = fragmentationBeforeBytes;
        SizeAfterBytes = sizeAfterBytes;
        FragmentationAfterBytes = fragmentationAfterBytes;
    }

    public long SizeBeforeBytes { get; }
    public long FragmentationBeforeBytes { get; }
    public long SizeAfterBytes { get; }
    public long FragmentationAfterBytes { get; }
}

/// <summary>Specifies the kind of a garbage collection.</summary>
public enum GCKind
{
    Any = 0,
    Ephemeral = 1,
    FullBlocking = 2,
    Background = 3,
}

/// <summary>Provides information about the collector's memory usage.</summary>
public readonly struct GCMemoryInfo
{
    private readonly long _heapSizeBytes;
    private readonly long _fragmentedBytes;
    private readonly long _index;
    private readonly long _pinnedObjectsCount;

    internal GCMemoryInfo(
        long heapSizeBytes,
        long fragmentedBytes,
        long index,
        long pinnedObjectsCount)
    {
        _heapSizeBytes = heapSizeBytes;
        _fragmentedBytes = fragmentedBytes;
        _index = index;
        _pinnedObjectsCount = pinnedObjectsCount;
    }

    // The collector currently exposes only heap size, free/live heap size,
    // collection count, and pinned-reference count. The remaining values use
    // the upstream snapshot's zero/empty "not available" representation until
    // the collector grows generation and pause-detail counters.
    public bool Compacted => false;
    public bool Concurrent => false;
    public long FinalizationPendingCount => 0;
    public long FragmentedBytes => _fragmentedBytes;
    public int Generation => 0;
    public ReadOnlySpan<GCGenerationInfo> GenerationInfo => ReadOnlySpan<GCGenerationInfo>.Empty;
    public long HeapSizeBytes => _heapSizeBytes;
    public long HighMemoryLoadThresholdBytes => 0;
    public long Index => _index;
    public long MemoryLoadBytes => 0;
    public ReadOnlySpan<TimeSpan> PauseDurations => ReadOnlySpan<TimeSpan>.Empty;
    public double PauseTimePercentage => 0;
    public long PinnedObjectsCount => _pinnedObjectsCount;
    public long PromotedBytes => 0;
    public long TotalAvailableMemoryBytes => 0;
    public long TotalCommittedBytes => _heapSizeBytes;
}
