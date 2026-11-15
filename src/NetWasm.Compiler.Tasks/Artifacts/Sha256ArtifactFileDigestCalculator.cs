// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
