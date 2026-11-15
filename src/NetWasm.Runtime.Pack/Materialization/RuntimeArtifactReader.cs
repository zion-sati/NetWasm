// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeArtifactReader : IRuntimeArtifactReader
{
    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }
}
