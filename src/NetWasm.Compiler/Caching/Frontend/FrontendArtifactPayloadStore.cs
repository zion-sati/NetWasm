using NetWasm.Compiler.Analysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Caching.Frontend;

internal sealed class FrontendArtifactMemoryStore
{
    internal object Gate { get; } = new();
    internal ConcurrentDictionary<string, ImmutableArray<byte>> Payloads { get; } = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<string, byte> LoadedNamespaces { get; } = new(StringComparer.Ordinal);
}

internal sealed class FrontendArtifactObjectStore
{
    internal object Gate { get; } = new();
    internal string? Namespace { get; set; }
    internal ConcurrentDictionary<string, FrontendArtifact> Artifacts { get; } = new(StringComparer.Ordinal);
}

internal sealed record FrontendArtifactObjectReadRequest(FrontendArtifactCacheContext Context, string Key);

internal interface IFrontendArtifactObjectReader
{
    bool TryRead(FrontendArtifactObjectReadRequest request, out FrontendArtifact artifact);
}

internal sealed class FrontendArtifactObjectReader(FrontendArtifactObjectStore store) : IFrontendArtifactObjectReader
{
    public bool TryRead(FrontendArtifactObjectReadRequest request, out FrontendArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.Equals(store.Namespace, request.Context.Namespace, StringComparison.Ordinal))
            return store.Artifacts.TryGetValue(request.Context.Namespace + "/" + request.Key, out artifact!);
        artifact = null!;
        return false;
    }
}

internal sealed record FrontendArtifactObjectPublication(
    FrontendArtifactCacheContext Context,
    ConcurrentDictionary<string, FrontendArtifact> Artifacts);

internal interface IFrontendArtifactObjectPublisher
{
    void Publish(FrontendArtifactObjectPublication publication);
}

internal sealed class FrontendArtifactObjectPublisher(FrontendArtifactObjectStore store) : IFrontendArtifactObjectPublisher
{
    public void Publish(FrontendArtifactObjectPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        lock (store.Gate)
        {
            if (!string.Equals(store.Namespace, publication.Context.Namespace, StringComparison.Ordinal))
            {
                store.Artifacts.Clear();
                store.Namespace = publication.Context.Namespace;
            }
            foreach (var pair in publication.Artifacts)
                store.Artifacts.TryAdd(publication.Context.Namespace + "/" + pair.Key, pair.Value);
        }
    }
}

internal sealed record FrontendArtifactPayloadReadRequest(FrontendArtifactCacheContext Context, string Key);

internal interface IFrontendArtifactPayloadReader
{
    bool TryRead(FrontendArtifactPayloadReadRequest request, out ImmutableArray<byte> payload);
}

internal sealed record FrontendArtifactPayloadPublication(
    FrontendArtifactCacheContext Context,
    ConcurrentDictionary<string, ImmutableArray<byte>> Payloads);

internal interface IFrontendArtifactPayloadPublisher
{
    void Publish(FrontendArtifactPayloadPublication publication);
}

internal sealed class FrontendArtifactPayloadReader(
    FrontendArtifactMemoryStore memory,
    IFrontendArtifactCacheRequestResolver requests) : IFrontendArtifactPayloadReader
{
    private const uint EnvelopeMagic = 0x3243434E;
    private const int ChecksumLength = 32;
    internal const int MaximumPayloadBytes = 4 * 1024 * 1024;
    private const long MaximumBundleBytes = 72L * 1024 * 1024;
    private const int MaximumArtifacts = 100_000;

    public bool TryRead(FrontendArtifactPayloadReadRequest request, out ImmutableArray<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(request);
        var active = requests.Resolve();
        if (active is null)
        {
            payload = default;
            return false;
        }
        var compositeKey = request.Context.Namespace + "/" + request.Key;
        if (memory.Payloads.TryGetValue(compositeKey, out payload))
        {
            ref var hits = ref memory.LoadedNamespaces.ContainsKey(request.Context.Namespace)
                ? ref active.DiskHits
                : ref active.MemoryHits;
            System.Threading.Interlocked.Increment(ref hits);
            return true;
        }
        if (request.Context.Directory is null) return false;
        lock (memory.Gate)
        {
            if (memory.Payloads.TryGetValue(compositeKey, out payload))
            {
                System.Threading.Interlocked.Increment(ref active.DiskHits);
                return true;
            }
            if (memory.LoadedNamespaces.ContainsKey(request.Context.Namespace)) return false;
            try
            {
                var path = Path.Combine(request.Context.Directory, "artifacts.bin");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
                if (stream.Length < sizeof(uint) + sizeof(int) || stream.Length > MaximumBundleBytes) return false;
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                if (reader.ReadUInt32() != EnvelopeMagic) return false;
                var count = reader.ReadInt32();
                if (count < 0 || count > MaximumArtifacts) return false;
                var loaded = new Dictionary<string, ImmutableArray<byte>>(count, StringComparer.Ordinal);
                for (var index = 0; index < count; index++)
                {
                    var key = reader.ReadString();
                    if (key.Length != 64) return false;
                    var length = reader.ReadInt32();
                    if (length < 0 || length > MaximumPayloadBytes || stream.Length - stream.Position < ChecksumLength + length) return false;
                    var checksum = reader.ReadBytes(ChecksumLength);
                    var bytes = reader.ReadBytes(length);
                    if (bytes.Length != length || !CryptographicOperations.FixedTimeEquals(checksum, SHA256.HashData(bytes))) return false;
                    if (!loaded.TryAdd(key, [.. bytes])) return false;
                }
                if (stream.Position != stream.Length) return false;
                foreach (var pair in loaded)
                    memory.Payloads.TryAdd(request.Context.Namespace + "/" + pair.Key, pair.Value);
                memory.LoadedNamespaces.TryAdd(request.Context.Namespace, 0);
                if (!memory.Payloads.TryGetValue(compositeKey, out payload)) return false;
                System.Threading.Interlocked.Increment(ref active.DiskHits);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}

internal sealed class FrontendArtifactPayloadPublisher : IFrontendArtifactPayloadPublisher
{
    private const uint EnvelopeMagic = 0x3243434E;

    public void Publish(FrontendArtifactPayloadPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.Context.Directory is null || publication.Payloads.IsEmpty) return;
        try
        {
            Directory.CreateDirectory(publication.Context.Directory);
            using var cacheLock = new FileStream(Path.Combine(publication.Context.Directory, ".publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            WriteAtomically(publication.Context.Directory, publication.Payloads);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cache publication is opportunistic and cannot replace compile success.
        }
    }

    private static void WriteAtomically(
        string directory,
        ConcurrentDictionary<string, ImmutableArray<byte>> payloads)
    {
        var destination = Path.Combine(directory, "artifacts.bin");
        var temporary = Path.Combine(directory, ".artifacts." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(EnvelopeMagic);
                writer.Write(payloads.Count);
                foreach (var pair in payloads.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value.Length);
                    writer.Write(SHA256.HashData(pair.Value.AsSpan()));
                    writer.Write(pair.Value.AsSpan());
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { }
        }
    }
}
