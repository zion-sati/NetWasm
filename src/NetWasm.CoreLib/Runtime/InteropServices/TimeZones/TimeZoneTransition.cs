// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct TimeZoneTransition
    {
        internal TimeZoneTransition(long unixSeconds, int offsetSeconds, bool isDaylightSavingTime)
        {
            UnixSeconds = unixSeconds;
            OffsetSeconds = offsetSeconds;
            IsDaylightSavingTime = isDaylightSavingTime;
        }

        internal long UnixSeconds { get; }
        internal int OffsetSeconds { get; }
        internal bool IsDaylightSavingTime { get; }
    }
}
