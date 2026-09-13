using System;
using System.Security.Cryptography;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IArtifactDigestCalculator
{
    string Calculate(ReadOnlySpan<byte> bytes);
}

public sealed class ArtifactDigestCalculator : IArtifactDigestCalculator
{
    public string Calculate(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
