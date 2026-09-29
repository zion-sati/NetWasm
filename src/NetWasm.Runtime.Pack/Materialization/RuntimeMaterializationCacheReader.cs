using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMaterializationCacheReader : IRuntimeMaterializationCacheReader
{
    private static readonly byte[] Magic = "NWRCACHE"u8.ToArray();
    private const int SchemaVersion = 1;

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
                sha256.Length != 64 || !sha256.All(Uri.IsHexDigit) ||
                stream.Length - stream.Position != length)
            {
                return Corrupt();
            }

            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            if (!string.Equals(
                    CalculateDigest(bytes),
                    sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Corrupt();
            }

            return new(
                RuntimeMaterializationCacheOutcome.Hit,
                bytes,
                sha256.ToLowerInvariant());
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or FormatException)
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
