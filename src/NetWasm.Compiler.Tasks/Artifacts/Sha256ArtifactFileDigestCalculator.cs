using System.Security.Cryptography;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class Sha256ArtifactFileDigestCalculator : IArtifactFileDigestCalculator
{
    public string Calculate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
