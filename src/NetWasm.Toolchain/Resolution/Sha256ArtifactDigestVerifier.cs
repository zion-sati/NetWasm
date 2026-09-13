using System;
using System.IO;
using System.Security.Cryptography;

namespace NetWasm.Toolchain.Resolution;

public sealed class Sha256ArtifactDigestVerifier : IArtifactDigestVerifier
{
    public void Verify(string path, string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Refusing to verify a symbolic or reparse-point asset '{path}'.");
        }

        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 digest mismatch for '{path}'.");
        }
    }
}
