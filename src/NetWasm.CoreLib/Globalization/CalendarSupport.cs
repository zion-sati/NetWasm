// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm support for the calendars ported from dotnet/runtime commit
// 474ed963699fc29809015392738a15231aaae2de. Upstream calendars call internal DateTime and
// DateOnly members and one LocalAppContextSwitches value that NetWasm's types do not declare;
// these give the same results from NetWasm's own date arithmetic.

namespace System.Globalization
{
    internal static class CalendarDateParts
    {
        // DateTime.GetDate
        internal static void GetDate(this DateTime time, out int year, out int month, out int day) =>
            GregorianDateMath.GetDateParts((int)(time.Ticks / TimeSpan.TicksPerDay), out year, out month, out day);

        // DateTime.GetTime(out hour, out minute, out second, out millisecond)
        internal static void GetTime(this DateTime time, out int hour, out int minute, out int second, out int millisecond)
        {
            ulong milliseconds = (ulong)time.Ticks / TimeSpan.TicksPerMillisecond;
            ulong seconds = milliseconds / 1000;
            millisecond = (int)(milliseconds - (seconds * 1000));
            ulong minutes = seconds / 60;
            second = (int)(seconds - (minutes * 60));
            ulong hours = minutes / 60;
            minute = (int)(minutes - (hours * 60));
            hour = (int)((uint)hours % 24);
        }

        // DateOnly.GetEquivalentDateTime
        internal static DateTime GetEquivalentDateTime(this DateOnly date) =>
            new DateTime(date.DayNumber * TimeSpan.TicksPerDay);
    }
}

namespace System
{
    internal static partial class LocalAppContextSwitches
    {
        // Upstream caches the switch; the calendars read it only on era-range edge cases.
        internal static bool EnforceJapaneseEraYearRanges =>
            AppContext.TryGetSwitch("Switch.System.Globalization.EnforceJapaneseEraYearRanges", out bool enabled) && enabled;
    }
}
