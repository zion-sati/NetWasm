using System.Security.Cryptography;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeMaterializationCacheStoreTests
{
    private static readonly RuntimeMaterializationCacheKey Key = new(new string('a', 64));
    private static readonly RuntimeMaterializationCacheSlot Slot =
        new("wasm32", RuntimeWasmOptimization.Oz);

    [Fact]
    public void RoundTripsIntegrityBoundNativeLayoutAndProviderEvidence()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 1, 2, 3 };
        var evidence = new RuntimeNativeCacheEvidence(
            new("wasm32", 65_552, 100_000, 100_000, 165_536, 165_536, 262_144, 2_147_483_648),
            [new("sum", "/native/libmule.a", RuntimePackTestData.Digest, "mule.o")]);

        new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, Digest(bytes), evidence);
        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, result.Outcome);
        Assert.Equal(bytes, result.Bytes);
        Assert.Equal(evidence.Layout, result.NativeEvidence!.Layout);
        Assert.Equal(evidence.Bindings.ToArray(), result.NativeEvidence.Bindings.ToArray());
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        var envelope = File.ReadAllBytes(path);
        var marker = System.Text.Encoding.UTF8.GetBytes("mule.o");
        var position = envelope.AsSpan().IndexOf(marker);
        Assert.True(position >= 0);
        envelope[position] ^= 1;
        File.WriteAllBytes(path, envelope);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt,
            new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key).Outcome);
    }

    [Fact]
    public void MissingEntryIsAMiss()
    {
        using var directory = new TemporaryDirectory();

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, result.Outcome);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public void StoredKeyCannotBeReplacedWithoutInvalidatingTheWholeEnvelope()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 42 };
        new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, Digest(bytes));
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        var envelope = File.ReadAllBytes(path);
        var offset = envelope.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(Key.Value));
        Assert.True(offset >= 0);
        System.Text.Encoding.UTF8.GetBytes(new string('b', 64)).CopyTo(envelope.AsSpan(offset, 64));
        File.WriteAllBytes(path, envelope);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, new RuntimeMaterializationCacheReader().Read(
            directory.Path, Slot, new(new string('b', 64))).Outcome);
    }

    [Fact]
    public void ValidEvidenceAndModuleChecksumsCannotQualifyASplicedEnvelope()
    {
        using var directory = new TemporaryDirectory();
        var first = new byte[] { 1, 2, 3 };
        var replacement = new byte[] { 4, 5, 6 };
        var evidence = new RuntimeNativeCacheEvidence(
            new("wasm32", 65_552, 100_000, 100_000, 165_536, 165_536, 262_144, 2_147_483_648), []);
        new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, first, Digest(first), evidence);
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        var envelope = File.ReadAllBytes(path);
        using (var stream = new MemoryStream(envelope))
        using (var reader = new BinaryReader(stream))
        {
            reader.ReadBytes(8);
            reader.ReadInt32();
            reader.ReadString();
            reader.ReadInt64();
            var digestOffset = checked((int)stream.Position + 1); // The 64-byte string uses a one-byte length prefix.
            Assert.Equal(Digest(first), reader.ReadString());
            var evidenceLength = reader.ReadInt32();
            reader.ReadString();
            reader.ReadBytes(evidenceLength);
            var moduleOffset = checked((int)stream.Position);
            System.Text.Encoding.UTF8.GetBytes(Digest(replacement)).CopyTo(envelope.AsSpan(digestOffset, 64));
            replacement.CopyTo(envelope.AsSpan(moduleOffset, replacement.Length));
        }
        File.WriteAllBytes(path, envelope);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt,
            new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key).Outcome);
    }

    [Fact]
    public void UnavailableEntryIsAMiss()
    {
        using var directory = new TemporaryDirectory();
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        Directory.CreateDirectory(path);

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss, result.Outcome);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public void RoundTripsValidatedBytes()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0, 97, 115, 109, 42 };
        var digest = Digest(bytes);

        new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, digest);
        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, result.Outcome);
        Assert.Equal(bytes, result.Bytes);
        Assert.Equal(digest, result.Sha256);
    }

    [Fact]
    public void DifferentSemanticKeyReplacesTheSameSlot()
    {
        using var directory = new TemporaryDirectory();
        var replacementKey = new RuntimeMaterializationCacheKey(new string('b', 64));
        var first = new byte[] { 1 };
        var replacement = new byte[] { 2 };
        var writer = new RuntimeMaterializationCacheWriter();
        var reader = new RuntimeMaterializationCacheReader();

        writer.Write(directory.Path, Slot, Key, first, Digest(first));
        writer.Write(directory.Path, Slot, replacementKey, replacement, Digest(replacement));

        Assert.Equal(RuntimeMaterializationCacheOutcome.Miss,
            reader.Read(directory.Path, Slot, Key).Outcome);
        var current = reader.Read(directory.Path, Slot, replacementKey);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, current.Outcome);
        Assert.Equal(replacement, current.Bytes);
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*.nwcache", SearchOption.AllDirectories));
    }

    [Fact]
    public void TargetAndModeUseSeparateFiniteSlots()
    {
        using var directory = new TemporaryDirectory();
        var writer = new RuntimeMaterializationCacheWriter();
        var bytes = new byte[] { 42 };
        var slots = new[]
        {
            new RuntimeMaterializationCacheSlot("wasm32", RuntimeWasmOptimization.None),
            new RuntimeMaterializationCacheSlot("wasm32", RuntimeWasmOptimization.Oz),
            new RuntimeMaterializationCacheSlot("wasm64", RuntimeWasmOptimization.None),
            new RuntimeMaterializationCacheSlot("wasm64", RuntimeWasmOptimization.Oz),
        };

        foreach (var slot in slots)
        {
            writer.Write(directory.Path, slot, Key, bytes, Digest(bytes));
        }

        Assert.Equal(slots.Length,
            Directory.EnumerateFiles(directory.Path, "*.nwcache", SearchOption.AllDirectories).Count());
        Assert.All(slots, slot => Assert.Equal(RuntimeMaterializationCacheOutcome.Hit,
            new RuntimeMaterializationCacheReader().Read(directory.Path, slot, Key).Outcome));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../wasm32")]
    [InlineData("wasm32/path")]
    public void RejectsInvalidSlotTarget(string target)
    {
        using var directory = new TemporaryDirectory();
        var slot = new RuntimeMaterializationCacheSlot(target, RuntimeWasmOptimization.Oz);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeMaterializationCachePaths.Entry(directory.Path, slot));

        Assert.Equal("The NetWasm runtime cache slot is invalid.", exception.Message);
    }

    [Fact]
    public void TruncatedEntryIsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0, 97, 115, 109, 42 };
        new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, Digest(bytes));
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..^1]);

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.Outcome);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public void MalformedStringHeaderIsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("NWRCACHE"u8.ToArray());
            writer.Write(3);
            writer.Write(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 });
        }

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.Outcome);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void InvalidStoredKeyIsCorrupt(string storedKey)
    {
        using var directory = new TemporaryDirectory();
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("NWRCACHE"u8.ToArray());
            writer.Write(3);
            writer.Write(storedKey);
        }

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.Outcome);
    }

    [Theory]
    [InlineData(InvalidEntryMetadata.NegativeLength)]
    [InlineData(InvalidEntryMetadata.OversizedLength)]
    [InlineData(InvalidEntryMetadata.ShortDigest)]
    [InlineData(InvalidEntryMetadata.NonHexDigest)]
    public void InvalidEntryMetadataIsCorrupt(InvalidEntryMetadata invalid)
    {
        using var directory = new TemporaryDirectory();
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("NWRCACHE"u8.ToArray());
            writer.Write(3);
            writer.Write(Key.Value);
            writer.Write(invalid switch
            {
                InvalidEntryMetadata.NegativeLength => -1L,
                InvalidEntryMetadata.OversizedLength => (long)int.MaxValue + 1,
                _ => 0L,
            });
            writer.Write(invalid switch
            {
                InvalidEntryMetadata.ShortDigest => "0",
                InvalidEntryMetadata.NonHexDigest => new string('g', 64),
                _ => new string('0', 64),
            });
        }

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.Outcome);
    }

    [Fact]
    public void DigestMismatchIsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0, 97, 115, 109, 42 };
        new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, Digest(bytes));
        var path = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        var entry = File.ReadAllBytes(path);
        // Corrupt a module byte, not the separate envelope digest trailer.
        entry[^(SHA256.HashSizeInBytes + 1)] ^= 0xff;
        File.WriteAllBytes(path, entry);

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);

        Assert.Equal(RuntimeMaterializationCacheOutcome.Corrupt, result.Outcome);
    }

    [Fact]
    public void FailedAtomicMoveRemovesTemporarySibling()
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0, 97, 115, 109, 42 };
        var entry = RuntimeMaterializationCachePaths.Entry(directory.Path, Slot);
        Directory.CreateDirectory(entry);

        Assert.ThrowsAny<IOException>(() =>
            new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, Digest(bytes)));

        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(entry)!, "*.tmp"));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void RejectsInvalidDigestBeforePublishing(string digest)
    {
        using var directory = new TemporaryDirectory();
        var bytes = new byte[] { 0, 97, 115, 109, 42 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, digest));

        Assert.Equal("The NetWasm runtime cache artifact digest is invalid.", exception.Message);
        Assert.False(File.Exists(RuntimeMaterializationCachePaths.Entry(directory.Path, Slot)));
    }

    [Fact]
    public async Task ConcurrentIdenticalPublicationsConverge()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray();
        var digest = Digest(bytes);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            new RuntimeMaterializationCacheWriter().Write(directory.Path, Slot, Key, bytes, digest))));

        var result = new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, Key);
        Assert.Equal(RuntimeMaterializationCacheOutcome.Hit, result.Outcome);
        Assert.Equal(bytes, result.Bytes);
        Assert.Single(Directory.EnumerateFiles(
            Path.GetDirectoryName(RuntimeMaterializationCachePaths.Entry(directory.Path, Slot))!));
    }

    [Fact]
    public async Task ConcurrentReplacementsLeaveOneCompleteRecord()
    {
        using var directory = new TemporaryDirectory();
        var keys = new[] { Key, new RuntimeMaterializationCacheKey(new string('b', 64)) };
        var payloads = new[] { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 } };

        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
            new RuntimeMaterializationCacheWriter().Write(
                directory.Path,
                Slot,
                keys[index % 2],
                payloads[index % 2],
                Digest(payloads[index % 2])))));

        var reads = keys.Select(key =>
            new RuntimeMaterializationCacheReader().Read(directory.Path, Slot, key)).ToArray();
        Assert.Single(reads, read => read.Outcome == RuntimeMaterializationCacheOutcome.Hit);
        Assert.Single(reads, read => read.Outcome == RuntimeMaterializationCacheOutcome.Miss);
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*.nwcache", SearchOption.AllDirectories));
    }

    private static string Digest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public enum InvalidEntryMetadata
    {
        NegativeLength,
        OversizedLength,
        ShortDigest,
        NonHexDigest,
    }
}
