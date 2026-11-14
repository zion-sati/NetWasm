// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    using Runtime.InteropServices;
    using Runtime.InteropServices.TimeZones;

    // NetWasm: upstream's DateTimeFormat and DateTimeParse read TimeZoneInfo.Local. These read the platform's
    // local-time service instead, the offsets DateTime.ToLocalTime and ToUniversalTime use, so formatting and parsing
    // a date do not build the time-zone catalog.
    public sealed partial class TimeZoneInfo
    {
        // The local zone's offset for a time: a UTC time's instant, or a local or unspecified time read as local.
        internal static TimeSpan GetLocalUtcOffset(DateTime dateTime, TimeZoneInfoOptions flags)
        {
            PlatformServices.EnsureLocalTimeReady();
            var basis = dateTime.Kind == DateTimeKind.Utc
                ? LocalTimeBasis.Utc
                : dateTime.IsAmbiguousDaylightSavingTime()
                    ? LocalTimeBasis.LocalAmbiguousDaylight
                    : LocalTimeBasis.Local;
            return new TimeSpan(PlatformServices.LocalTimeOffsets.Resolve(dateTime.Ticks, basis).Ticks);
        }

        // The local zone's offset at a UTC instant, and whether the local time it gives is the daylight half of an
        // ambiguous hour.
        internal static TimeSpan GetLocalUtcOffsetFromUtc(DateTime utcDateTime, out bool isAmbiguousLocalDst)
        {
            PlatformServices.EnsureLocalTimeReady();
            var offset = PlatformServices.LocalTimeOffsets.Resolve(utcDateTime.Ticks, LocalTimeBasis.Utc);
            isAmbiguousLocalDst = offset.IsAmbiguousDaylightSavingTime;
            return new TimeSpan(offset.Ticks);
        }
    }
}
