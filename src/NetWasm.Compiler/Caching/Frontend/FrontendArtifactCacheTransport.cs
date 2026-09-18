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
    internal const long MaximumWorkingSetBytes = 72L * 1024 * 1024;
    internal const int MaximumPublicationBatchEntries = 256;
    internal const long MaximumPublicationBatchBytes = 4L * 1024 * 1024;
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
                    pair.Value.Length > FrontendArtifactCachePolicy.MaximumWorkingSetBytes -
                        active.PublishedBytes) continue;
                active.Published[pair.Key] = pair.Value;
                active.PublishedBytes += pair.Value.Length;
            }
        }
    }
}

internal sealed class FrontendArtifactPayloadPublicationFanout(
    FrontendArtifactPayloadPublisher disk,
    FrontendArtifactTransportPublisher transport) : IFrontendArtifactPayloadPublisher
{
    public void Publish(FrontendArtifactPayloadPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        transport.Publish(publication);
        disk.Publish(publication);
    }
}

internal sealed class FrontendArtifactCacheTransport(
    IFrontendArtifactCacheIdentityBuilder identities,
    FrontendArtifactMemoryStore memory,
    FrontendArtifactObjectStore objects,
    FrontendArtifactTransportStore transport) : IFrontendArtifactCacheTransport
{
    public FrontendArtifactCacheDescriptor? Prepare(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var context = identities.Build(options);
        if (context is null) return null;
        var token = Guid.NewGuid().ToString("N");
        lock (transport.Gate)
        {
            if (transport.Prepared is not null || transport.Active.Value is not null)
                throw new InvalidOperationException("A frontend artifact compilation is already prepared.");
            transport.Prepared = new(token, context);
        }
        return new(FrontendArtifactCacheIdentityBuilder.Schema, context.Namespace, token);
    }

    public IDisposable BeginCompilation(FrontendArtifactCacheDescriptor descriptor,
        IReadOnlyList<FrontendArtifactCacheEntry> entries)
    {
        Validate(descriptor);
        ArgumentNullException.ThrowIfNull(entries);
        lock (transport.Gate)
        {
            var prepared = transport.Prepared is { } candidate &&
                string.Equals(candidate.Token, descriptor.PreparationToken,
                    StringComparison.Ordinal) &&
                string.Equals(candidate.Context.Namespace, descriptor.Namespace,
                    StringComparison.Ordinal)
                ? candidate : throw new InvalidOperationException(
                    "The frontend artifact preparation is stale or invalid.");
            transport.Prepared = null;
            memory.Payloads.Clear();
            memory.LoadedNamespaces.Clear();
            objects.Artifacts.Clear();
            objects.Namespace = null;
            var active = new ActiveFrontendArtifactCache(prepared.Context);
            transport.Active.Value = active;
            ImportBestEffort(descriptor, entries);
            return new FrontendArtifactCompilationScope(memory, objects, transport, active);
        }
    }

    public void CancelPreparation(FrontendArtifactCacheDescriptor descriptor)
    {
        Validate(descriptor);
        lock (transport.Gate)
        {
            if (transport.Prepared is not { } prepared || !string.Equals(prepared.Token,
                    descriptor.PreparationToken, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The frontend artifact preparation is stale or invalid.");
            transport.Prepared = null;
        }
    }

    public FrontendArtifactCachePublication? CompleteCompilation(
        FrontendArtifactCacheDescriptor descriptor)
    {
        Validate(descriptor);
        lock (transport.Gate)
        {
            var active = transport.Active.Value;
            if (active is null || !string.Equals(active.Context.Namespace,
                    descriptor.Namespace, StringComparison.Ordinal))
                throw new InvalidOperationException("No matching frontend artifact compilation is active.");
            if (active.Published.IsEmpty) return null;
            if (transport.Publication is not null)
                throw new InvalidOperationException(
                    "The previous frontend artifact publication is still active.");
            var token = Guid.NewGuid().ToString("N");
            transport.Publication = new(token, descriptor.Schema, descriptor.Namespace,
                new(active.Published, StringComparer.Ordinal), active.PublishedBytes);
            return new(token, active.Published.Count, active.PublishedBytes);
        }
    }

    public FrontendArtifactCacheBatch ReadBatch(FrontendArtifactCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (transport.Gate)
        {
            var state = RequirePublication(publication);
            if (state.OutstandingBatch is not null) return Copy(state.OutstandingBatch);
            var selected = new List<KeyValuePair<string, ImmutableArray<byte>>>();
            long bytes = 0;
            foreach (var pair in state.Entries.OrderBy(static pair => pair.Key,
                         StringComparer.Ordinal))
            {
                if (selected.Count != 0 && (selected.Count >=
                        FrontendArtifactCachePolicy.MaximumPublicationBatchEntries ||
                    pair.Value.Length > FrontendArtifactCachePolicy.MaximumPublicationBatchBytes -
                        bytes)) break;
                selected.Add(pair);
                bytes += pair.Value.Length;
            }
            var entries = selected.Select(pair => Entry(state.Schema, state.Namespace,
                pair.Key, pair.Value)).ToArray();
            var batch = new FrontendArtifactCacheBatch(publication.Token,
                Guid.NewGuid().ToString("N"), entries, selected.Count == state.Entries.Count);
            state.OutstandingKeys = [.. selected.Select(static pair => pair.Key)];
            state.OutstandingBatch = batch;
            return Copy(batch);
        }
    }

    public void AcknowledgeBatch(FrontendArtifactCachePublication publication,
        FrontendArtifactCacheBatch batch)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(batch);
        lock (transport.Gate)
        {
            var state = RequirePublication(publication);
            if (state.OutstandingBatch is null || !string.Equals(
                    state.OutstandingBatch.BatchToken, batch.BatchToken,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("The frontend artifact batch is stale or invalid.");
            foreach (var key in state.OutstandingKeys)
                if (state.Entries.TryRemove(key, out var removed))
                    state.TotalBytes -= removed.Length;
            state.OutstandingBatch = null;
            state.OutstandingKeys = default;
            if (state.Entries.IsEmpty) transport.Publication = null;
        }
    }

    public void Abandon(FrontendArtifactCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (transport.Gate)
        {
            _ = RequirePublication(publication);
            transport.Publication = null;
        }
    }

    private void ImportBestEffort(FrontendArtifactCacheDescriptor descriptor,
        IReadOnlyList<FrontendArtifactCacheEntry> entries)
    {
        if (entries.Count > FrontendArtifactCachePolicy.MaximumArtifacts) return;
        long total = 0;
        var validated = new Dictionary<string, ImmutableArray<byte>>(entries.Count,
            StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || entry.Payload is null || entry.Checksum is null ||
                !IsHash(entry.Key) || entry.Payload.Length >
                    FrontendArtifactPayloadReader.MaximumPayloadBytes ||
                entry.Checksum.Length != SHA256.HashSizeInBytes ||
                !CryptographicOperations.FixedTimeEquals(entry.Checksum,
                    Checksum(descriptor.Schema, descriptor.Namespace, entry.Key,
                        entry.Payload)) || !validated.TryAdd(entry.Key,
                    ImmutableArray.Create(entry.Payload))) return;
            total += entry.Payload.Length;
            if (total > FrontendArtifactCachePolicy.MaximumWorkingSetBytes) return;
        }
        foreach (var pair in validated)
            memory.Payloads[descriptor.Namespace + "/" + pair.Key] = pair.Value;
        memory.LoadedNamespaces.TryAdd(descriptor.Namespace, 0);
    }

    private PublishedFrontendArtifactCache RequirePublication(
        FrontendArtifactCachePublication publication) =>
        transport.Publication is { } state && string.Equals(state.Token,
            publication.Token, StringComparison.Ordinal) && state.TotalBytes <=
            publication.TotalBytes
            ? state : throw new InvalidOperationException(
                "The frontend artifact publication is stale or invalid.");

    private static FrontendArtifactCacheEntry Entry(string schema, string cacheNamespace,
        string key, ImmutableArray<byte> payload) => new(key, payload.ToArray(),
        Checksum(schema, cacheNamespace, key, payload.AsSpan()));

    private static FrontendArtifactCacheBatch Copy(FrontendArtifactCacheBatch batch) =>
        batch with
        {
            Entries = batch.Entries.Select(entry => entry with
            {
                Payload = (byte[])entry.Payload.Clone(),
                Checksum = (byte[])entry.Checksum.Clone(),
            }).ToArray(),
        };

    private static byte[] Checksum(string schema, string cacheNamespace, string key,
        ReadOnlySpan<byte> payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, schema);
        Append(hash, cacheNamespace);
        Append(hash, key);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static void Validate(FrontendArtifactCacheDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.Schema, FrontendArtifactCacheIdentityBuilder.Schema,
                StringComparison.Ordinal) || !IsHash(descriptor.Namespace) ||
            descriptor.PreparationToken is not { Length: 32 })
            throw new ArgumentException("The frontend artifact descriptor is invalid.",
                nameof(descriptor));
    }

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed class FrontendArtifactCompilationScope(
        FrontendArtifactMemoryStore memory,
        FrontendArtifactObjectStore objects,
        FrontendArtifactTransportStore transport,
        ActiveFrontendArtifactCache active) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            lock (transport.Gate)
            {
                if (ReferenceEquals(transport.Active.Value, active))
                    transport.Active.Value = null;
                memory.Payloads.Clear();
                memory.LoadedNamespaces.Clear();
                objects.Artifacts.Clear();
                objects.Namespace = null;
            }
            _disposed = true;
        }
    }
}
