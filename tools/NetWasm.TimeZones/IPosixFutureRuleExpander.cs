// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal interface IPosixFutureRuleExpander
{
    ImmutableArray<TimeZoneTransition> Expand(
        string rule,
        long afterUnixSeconds);
}
