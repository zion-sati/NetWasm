// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimePackManifestReader(IRuntimePackManifestParser parser) : IRuntimePackManifestReader
{
    private readonly IRuntimePackManifestParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));

    public RuntimePackManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The NetWasm runtime pack manifest is missing.");
        }

        return _parser.Parse(File.ReadAllText(path));
    }
}
