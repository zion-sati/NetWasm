// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal interface ITimeZoneSelectionSelector
{
    ImmutableArray<TimeZoneDefinition> Select(
        TimeZoneCatalog catalog,
        IReadOnlyCollection<string> requestedZones,
        bool includeAll);
}
