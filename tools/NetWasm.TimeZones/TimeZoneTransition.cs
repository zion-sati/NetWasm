// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.TimeZones;

internal readonly record struct TimeZoneTransition(
    long UnixSeconds,
    int OffsetSeconds,
    bool IsDaylightSavingTime);
