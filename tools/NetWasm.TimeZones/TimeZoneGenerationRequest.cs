// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed record TimeZoneGenerationRequest(
    string SourceDirectory,
    string AssetPath,
    string BrotliPath,
    string ManifestPath,
    string BrowserLoaderPath,
    ImmutableArray<string> Zones,
    bool IncludeAll);
