// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;
using NetWasm.Sdk.Pack;

namespace NetWasm.Sdk.Pack.Packing;

public sealed class PackManifestSerializer : IPackManifestSerializer
{
    private static readonly SdkPackJsonContext Json = new(new()
    {
        WriteIndented = true,
        NewLine = "\n",
    });

    public byte[] Serialize(CanonicalPackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Json.CanonicalPackManifest);
    }
}
