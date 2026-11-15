// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Security.Cryptography;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class Sha256ArtifactDigestCalculator : IArtifactDigestCalculator
{
    public string Calculate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
