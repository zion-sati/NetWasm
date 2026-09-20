using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NetWasm.Compiler.Caching.Frontend;

internal static class FrontendArtifactCachePolicy
{
    internal const int MaximumArtifacts = 100_000;
    internal const long MaximumEncodedWorkingSetBytes = 72L * 1024 * 1024;
    internal const long MaximumStructuralWorkingSetBytes = 96L * 1024 * 1024;
    internal const int MaximumPublicationBatchEntries = 256;
    internal const long MaximumPublicationBatchBytes = 4L * 1024 * 1024;

    internal static long EstimateStructuralBytes(int payloadBytes) =>
        checked((long)payloadBytes * 4 + 1024);
}

internal sealed class FrontendArtifactWorkingSetBudget
{
    private readonly object _gate = new();
    private int _artifacts;
    private long _encodedBytes;
    private long _structuralBytes;

    internal (int Artifacts, long EncodedBytes, long StructuralBytes) Read()
    {
        lock (_gate) return (_artifacts, _encodedBytes, _structuralBytes);
    }

    internal bool TryReserve(int artifacts, long encodedBytes, long structuralBytes)
    {
        if (artifacts < 0 || encodedBytes < 0 || structuralBytes < 0) return false;
        lock (_gate)
        {
            if (artifacts > FrontendArtifactCachePolicy.MaximumArtifacts - _artifacts ||
                encodedBytes > FrontendArtifactCachePolicy.MaximumEncodedWorkingSetBytes -
                    _encodedBytes ||
                structuralBytes >
                    FrontendArtifactCachePolicy.MaximumStructuralWorkingSetBytes -
                    _structuralBytes) return false;
            _artifacts += artifacts;
            _encodedBytes += encodedBytes;
            _structuralBytes += structuralBytes;
            return true;
        }
    }
}

internal sealed class FrontendArtifactTransportStore
{
    internal object Gate { get; } = new();
    internal PreparedFrontendArtifactCache? Prepared { get; set; }
    internal AsyncLocal<ActiveFrontendArtifactCache?> Active { get; } = new();
    internal PublishedFrontendArtifactCache? Publication { get; set; }
}

internal sealed record PreparedFrontendArtifactCache(
    string Token,
    FrontendArtifactCacheContext Context);

internal sealed class ActiveFrontendArtifactCache(FrontendArtifactCacheContext context)
{
    internal FrontendArtifactCacheContext Context { get; } = context;
    internal FrontendArtifactWorkingSetBudget Budget { get; } = new();
    internal ConcurrentDictionary<string, ImmutableArray<byte>> Published { get; } =
        new(StringComparer.Ordinal);
    internal long PublishedBytes;
}

internal sealed class PublishedFrontendArtifactCache(
    string token,
    string schema,
    string cacheNamespace,
    ConcurrentDictionary<string, ImmutableArray<byte>> entries,
    long totalBytes)
{
    internal string Token { get; } = token;
    internal string Schema { get; } = schema;
    internal string Namespace { get; } = cacheNamespace;
    internal ConcurrentDictionary<string, ImmutableArray<byte>> Entries { get; } = entries;
    internal long TotalBytes { get; set; } = totalBytes;
    internal FrontendArtifactCacheBatch? OutstandingBatch { get; set; }
    internal ImmutableArray<string> OutstandingKeys { get; set; }
}

internal sealed class FrontendArtifactTransportPublisher(
    FrontendArtifactTransportStore transport) : IFrontendArtifactPayloadPublisher
{
    public void Publish(FrontendArtifactPayloadPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var active = transport.Active.Value;
        if (active is null || !string.Equals(active.Context.Namespace,
                publication.Context.Namespace, StringComparison.Ordinal)) return;
        lock (transport.Gate)
        {
            foreach (var pair in publication.Payloads)
            {
                if (active.Published.ContainsKey(pair.Key) ||
                    active.Published.Count >= FrontendArtifactCachePolicy.MaximumArtifacts ||
                    pair.Value.Length > FrontendArtifactCachePolicy.MaximumEncodedWorkingSetBytes -
                        active.PublishedBytes) continue;
                active.Published[pair.Key] = pair.Value;
                active.PublishedBytes += pair.Value.Length;
            }
        }
    }
}

internal sealed class FrontendArtifactPayloadPublicationFanout(
    IFrontendArtifactPayloadPublisher disk,
    IFrontendArtifactPayloadPublisher transport) : IFrontendArtifactPayloadPublisher
{
    public void Publish(FrontendArtifactPayloadPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        transport.Publish(publication);
        disk.Publish(publication);
    }
}

internal sealed class FrontendArtifactCacheTransport(
    IFrontendArtifactCachePreparationFactory preparations,
    IFrontendArtifactCompilationFactory compilations,
    IFrontendArtifactPreparationCanceler canceler,
    IFrontendArtifactPublicationFactory publications,
    IFrontendArtifactPublicationBatchReader batches,
    IFrontendArtifactPublicationBatchAcknowledger acknowledgements,
    IFrontendArtifactPublicationAbandoner abandoner) : IFrontendArtifactCacheTransport
{
    public FrontendArtifactCacheDescriptor? Prepare(CompilerOptions options) =>
        preparations.Prepare(options);

    public IDisposable BeginCompilation(FrontendArtifactCacheDescriptor descriptor,
        IReadOnlyList<FrontendArtifactCacheEntry> entries)
        => compilations.Begin(descriptor, entries);

    public void CancelPreparation(FrontendArtifactCacheDescriptor descriptor)
        => canceler.Cancel(descriptor);

    public FrontendArtifactCachePublication? CompleteCompilation(
        FrontendArtifactCacheDescriptor descriptor)
        => publications.Complete(descriptor);

    public FrontendArtifactCacheBatch ReadBatch(FrontendArtifactCachePublication publication)
        => batches.Read(publication);

    public void AcknowledgeBatch(FrontendArtifactCachePublication publication,
        FrontendArtifactCacheBatch batch)
        => acknowledgements.Acknowledge(publication, batch);

    public void Abandon(FrontendArtifactCachePublication publication)
        => abandoner.Abandon(publication);
}
