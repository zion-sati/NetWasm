using System;
using System.IO;
using System.Security.Cryptography;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeNativeArchiveReader : IRuntimeNativeArchiveReader
{
    public string Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".a", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native libraries must be regular .a archives at absolute paths.");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[8];
            stream.ReadExactly(header);
            if (!header.SequenceEqual("!<arch>\n"u8))
                throw new InvalidOperationException("Native libraries must be regular archives; thin archives and other containers are unsupported.");
            stream.Position = 0;
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The selected native archive could not be read.", exception);
        }
    }
}
