// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
