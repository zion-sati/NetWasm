using System.Security.Cryptography;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusArtifactFingerprint
{
    string Compute(string path);
}

internal sealed class CorpusArtifactFingerprint : ICorpusArtifactFingerprint
{
    public string Compute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
