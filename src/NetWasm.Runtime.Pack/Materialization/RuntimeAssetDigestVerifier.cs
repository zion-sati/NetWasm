using System;
using System.IO;
using System.Security.Cryptography;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeAssetDigestVerifier : IRuntimeAssetDigestVerifier
{
    public void Verify(string path, string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("A required NetWasm runtime pack asset is missing.");
        }

        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A NetWasm runtime pack asset failed digest validation.");
        }
    }
}
