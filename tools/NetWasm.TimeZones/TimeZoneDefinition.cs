// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed record TimeZoneDefinition(
    string Name,
    int InitialOffsetSeconds,
    bool InitialDaylightSavingTime,
    ImmutableArray<TimeZoneTransition> Transitions);
