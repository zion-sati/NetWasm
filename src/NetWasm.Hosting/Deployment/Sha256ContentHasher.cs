using System;
using System.Security.Cryptography;

namespace NetWasm.Hosting.Deployment;

/// <summary>Computes canonical lowercase SHA-256 deployment identities.</summary>
public sealed class Sha256ContentHasher : IContentHasher
{
    public string Hash(ReadOnlyMemory<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content.Span));
}
