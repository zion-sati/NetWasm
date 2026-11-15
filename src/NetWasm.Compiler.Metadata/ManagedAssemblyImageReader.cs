// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;

namespace NetWasm.Compiler.Metadata;

public sealed class ManagedAssemblyImageReader : IManagedAssemblyImageReader
{
    public byte[] Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }
}
