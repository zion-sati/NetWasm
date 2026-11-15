// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed record TimeZoneCatalog(
    string DataVersion,
    ImmutableDictionary<string, TimeZoneDefinition> Definitions,
    ImmutableDictionary<string, string> Aliases);
