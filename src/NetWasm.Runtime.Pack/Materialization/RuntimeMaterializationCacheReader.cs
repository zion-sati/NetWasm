using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMaterializationCacheReader : IRuntimeMaterializationCacheReader
{
    private static readonly byte[] Magic = "NWRCACHE"u8.ToArray();
    private const int SchemaVersion = 3;

    public RuntimeMaterializationCacheRead Read(
        string cacheDirectory,
        RuntimeMaterializationCacheSlot slot,
        RuntimeMaterializationCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var path = RuntimeMaterializationCachePaths.Entry(cacheDirectory, slot);
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic) ||
                reader.ReadInt32() != SchemaVersion)
            {
                return Corrupt();
            }

            var storedKey = reader.ReadString();
            if (storedKey.Length != 64 || !storedKey.All(Uri.IsHexDigit))
            {
                return Corrupt();
            }
            if (!string.Equals(storedKey, key.Value, StringComparison.Ordinal))
            {
                return Miss();
            }

            var length = reader.ReadInt64();
            var sha256 = reader.ReadString();
            if (length < 0 || length > int.MaxValue ||
                sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            {
                return Corrupt();
            }

            var evidenceLength = reader.ReadInt32();
            var evidenceDigest = reader.ReadString();
            if (evidenceLength < 0 || evidenceDigest.Length != 64 || !evidenceDigest.All(Uri.IsHexDigit) ||
                stream.Length - stream.Position != (long)evidenceLength + length + SHA256.HashSizeInBytes)
                return Corrupt();
            var evidenceBytes = reader.ReadBytes(evidenceLength);
            if (!string.Equals(CalculateDigest(evidenceBytes), evidenceDigest, StringComparison.OrdinalIgnoreCase))
                return Corrupt();
            var nativeEvidence = JsonSerializer.Deserialize<RuntimeNativeCacheEvidence>(evidenceBytes);

            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (!string.Equals(
                    CalculateDigest(bytes),
                    sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Corrupt();
            }

            var payloadLength = stream.Position;
            var envelopeDigest = reader.ReadBytes(SHA256.HashSizeInBytes);
            stream.Position = 0;
            using var hashing = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            while (payloadLength > 0)
            {
                var count = (int)Math.Min(buffer.Length, payloadLength);
                stream.ReadExactly(buffer.AsSpan(0, count));
                hashing.AppendData(buffer, 0, count);
                payloadLength -= count;
            }
            if (!CryptographicOperations.FixedTimeEquals(hashing.GetHashAndReset(), envelopeDigest))
                return Corrupt();

            return new(
                RuntimeMaterializationCacheOutcome.Hit,
                bytes,
                sha256.ToLowerInvariant())
            {
                NativeEvidence = nativeEvidence,
            };
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or FormatException or JsonException)
        {
            return Corrupt();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Miss();
        }
    }

    private static RuntimeMaterializationCacheRead Miss() =>
        new(RuntimeMaterializationCacheOutcome.Miss, null, null);

    private static RuntimeMaterializationCacheRead Corrupt() =>
        new(RuntimeMaterializationCacheOutcome.Corrupt, null, null);

    private static string CalculateDigest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
