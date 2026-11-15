// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.TimeZones;

internal sealed record TimeZoneDeploymentManifest(
    int SchemaVersion,
    string DataVersion,
    string Identity,
    string AssetFile,
    int UncompressedBytes,
    string BrotliFile,
    int BrotliBytes,
    string BrowserLoaderFile,
    string[] Zones);
