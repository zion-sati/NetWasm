using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMaterializationCacheWriter : IRuntimeMaterializationCacheWriter
{
    private static readonly byte[] Magic = "NWRCACHE"u8.ToArray();
    private const int SchemaVersion = 1;

    public void Write(
        string cacheDirectory,
        RuntimeMaterializationCacheSlot slot,
        RuntimeMaterializationCacheKey key,
        byte[] bytes,
        string sha256)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(bytes);
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit) ||
            !string.Equals(CalculateDigest(bytes), sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The NetWasm runtime cache artifact digest is invalid.");
        }

        var path = RuntimeMaterializationCachePaths.Entry(cacheDirectory, slot);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                writer.Write(key.Value);
                writer.Write(bytes.LongLength);
                writer.Write(sha256.ToLowerInvariant());
                writer.Write(bytes);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CalculateDigest(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
