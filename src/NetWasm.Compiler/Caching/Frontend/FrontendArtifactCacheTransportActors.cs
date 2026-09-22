using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class FrontendArtifactCachePreparationFactory(
    IFrontendArtifactCacheIdentityBuilder identities,
    FrontendArtifactTransportStore transport) : IFrontendArtifactCachePreparationFactory
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
                throw new InvalidOperationException(
                    "A frontend artifact compilation is already prepared.");
            transport.Prepared = new(token, context);
        }
        return new(FrontendArtifactCacheIdentityBuilder.Schema, context.Namespace, token);
    }
}

internal sealed class FrontendArtifactCompilationFactory(
    FrontendArtifactMemoryStore memory,
    FrontendArtifactObjectStore objects,
    FrontendArtifactTransportStore transport) : IFrontendArtifactCompilationFactory
{
    public IDisposable Begin(FrontendArtifactCacheDescriptor descriptor,
        IReadOnlyList<FrontendArtifactCacheEntry> entries)
    {
        FrontendArtifactCacheTransportProtocol.Validate(descriptor);
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
            ClearStores();
            var active = new ActiveFrontendArtifactCache(prepared.Context);
            transport.Active.Value = active;
            ImportBestEffort(descriptor, entries, active.Budget);
            return new FrontendArtifactCompilationScope(ClearStores, transport, active);
        }
    }

    private void ImportBestEffort(FrontendArtifactCacheDescriptor descriptor,
        IReadOnlyList<FrontendArtifactCacheEntry> entries,
        FrontendArtifactWorkingSetBudget budget)
    {
        if (entries.Count > FrontendArtifactCachePolicy.MaximumArtifacts) return;
        long total = 0;
        var validated = new Dictionary<string, byte[]>(entries.Count,
            StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || entry.Payload is null || entry.Checksum is null ||
                !FrontendArtifactCacheTransportProtocol.IsHash(entry.Key) ||
                entry.Payload.Length > FrontendArtifactPayloadReader.MaximumPayloadBytes ||
                entry.Checksum.Length != SHA256.HashSizeInBytes ||
                !CryptographicOperations.FixedTimeEquals(entry.Checksum,
                    FrontendArtifactCacheTransportProtocol.Checksum(descriptor.Schema,
                        descriptor.Namespace, entry.Key, entry.Payload)) ||
                !validated.TryAdd(entry.Key, entry.Payload)) return;
            total += entry.Payload.Length;
            if (total > FrontendArtifactCachePolicy.MaximumEncodedWorkingSetBytes) return;
        }
        _ = budget.TryReserve(validated.Count, total, 0);
        foreach (var pair in validated)
            memory.Payloads[descriptor.Namespace + "/" + pair.Key] =
                ImmutableArray.Create(pair.Value);
        memory.LoadedNamespaces.TryAdd(descriptor.Namespace, 0);
        memory.Namespace = descriptor.Namespace;
        memory.EntryCount = validated.Count;
        memory.TotalBytes = total;
    }

    private void ClearStores()
    {
        memory.Payloads.Clear();
        memory.LoadedNamespaces.Clear();
        memory.Namespace = null;
        memory.EntryCount = 0;
        memory.TotalBytes = 0;
        objects.Artifacts.Clear();
        objects.StructuralBytes.Clear();
        objects.TotalStructuralBytes = 0;
        objects.Namespace = null;
    }

    private sealed class FrontendArtifactCompilationScope(
        Action clearStores,
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
                clearStores();
            }
            _disposed = true;
        }
    }
}

internal sealed class FrontendArtifactPreparationCanceler(
    FrontendArtifactTransportStore transport) : IFrontendArtifactPreparationCanceler
{
    public void Cancel(FrontendArtifactCacheDescriptor descriptor)
    {
        FrontendArtifactCacheTransportProtocol.Validate(descriptor);
        lock (transport.Gate)
        {
            if (transport.Prepared is not { } prepared || !string.Equals(prepared.Token,
                    descriptor.PreparationToken, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The frontend artifact preparation is stale or invalid.");
            transport.Prepared = null;
        }
    }
}

internal sealed class FrontendArtifactPublicationFactory(
    FrontendArtifactTransportStore transport) : IFrontendArtifactPublicationFactory
{
    public FrontendArtifactCachePublication? Complete(
        FrontendArtifactCacheDescriptor descriptor)
    {
        FrontendArtifactCacheTransportProtocol.Validate(descriptor);
        lock (transport.Gate)
        {
            var active = transport.Active.Value;
            if (active is null || !string.Equals(active.Context.Namespace,
                    descriptor.Namespace, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "No matching frontend artifact compilation is active.");
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
}

internal sealed class FrontendArtifactPublicationBatchReader(
    FrontendArtifactTransportStore transport) : IFrontendArtifactPublicationBatchReader
{
    public FrontendArtifactCacheBatch Read(FrontendArtifactCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (transport.Gate)
        {
            var state = FrontendArtifactCacheTransportProtocol.RequirePublication(
                transport, publication);
            if (state.OutstandingBatch is not null)
                return FrontendArtifactCacheTransportProtocol.Copy(state.OutstandingBatch);
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
            var entries = selected.Select(pair => new FrontendArtifactCacheEntry(pair.Key,
                pair.Value.ToArray(), FrontendArtifactCacheTransportProtocol.Checksum(
                    state.Schema, state.Namespace, pair.Key, pair.Value.AsSpan()))).ToArray();
            var batch = new FrontendArtifactCacheBatch(publication.Token,
                Guid.NewGuid().ToString("N"), entries, selected.Count == state.Entries.Count);
            state.OutstandingKeys = [.. selected.Select(static pair => pair.Key)];
            state.OutstandingBatch = batch;
            return FrontendArtifactCacheTransportProtocol.Copy(batch);
        }
    }
}

internal sealed class FrontendArtifactPublicationBatchAcknowledger(
    FrontendArtifactTransportStore transport) :
    IFrontendArtifactPublicationBatchAcknowledger
{
    public void Acknowledge(FrontendArtifactCachePublication publication,
        FrontendArtifactCacheBatch batch)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(batch);
        lock (transport.Gate)
        {
            var state = FrontendArtifactCacheTransportProtocol.RequirePublication(
                transport, publication);
            if (state.OutstandingBatch is null || !string.Equals(
                    state.OutstandingBatch.BatchToken, batch.BatchToken,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The frontend artifact batch is stale or invalid.");
            foreach (var key in state.OutstandingKeys)
                if (state.Entries.TryRemove(key, out var removed))
                    state.TotalBytes -= removed.Length;
            state.OutstandingBatch = null;
            state.OutstandingKeys = default;
            if (state.Entries.IsEmpty) transport.Publication = null;
        }
    }
}

internal sealed class FrontendArtifactPublicationAbandoner(
    FrontendArtifactTransportStore transport) : IFrontendArtifactPublicationAbandoner
{
    public void Abandon(FrontendArtifactCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (transport.Gate)
        {
            _ = FrontendArtifactCacheTransportProtocol.RequirePublication(
                transport, publication);
            transport.Publication = null;
        }
    }
}

internal static class FrontendArtifactCacheTransportProtocol
{
    internal static PublishedFrontendArtifactCache RequirePublication(
        FrontendArtifactTransportStore transport,
        FrontendArtifactCachePublication publication) =>
        transport.Publication is { } state && string.Equals(state.Token,
            publication.Token, StringComparison.Ordinal) && state.TotalBytes <=
            publication.TotalBytes
            ? state : throw new InvalidOperationException(
                "The frontend artifact publication is stale or invalid.");

    internal static FrontendArtifactCacheBatch Copy(FrontendArtifactCacheBatch batch) =>
        batch with
        {
            Entries = batch.Entries.Select(entry => entry with
            {
                Payload = (byte[])entry.Payload.Clone(),
                Checksum = (byte[])entry.Checksum.Clone(),
            }).ToArray(),
        };

    internal static byte[] Checksum(string schema, string cacheNamespace, string key,
        ReadOnlySpan<byte> payload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, schema);
        Append(hash, cacheNamespace);
        Append(hash, key);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    internal static void Validate(FrontendArtifactCacheDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.Schema, FrontendArtifactCacheIdentityBuilder.Schema,
                StringComparison.Ordinal) || !IsHash(descriptor.Namespace) ||
            descriptor.PreparationToken is not { Length: 32 })
            throw new ArgumentException("The frontend artifact descriptor is invalid.",
                nameof(descriptor));
    }

    internal static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }
}
