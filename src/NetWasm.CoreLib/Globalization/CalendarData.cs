// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's stand-in for dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce's CalendarData.
// NetWasm runs upstream's invariant globalization mode only, where CalendarData answers from
// CalendarData.Invariant: the current era is 1 and the two-digit-year maximum is 2049 for every
// calendar. Calendars use only these two queries.

namespace System.Globalization
{
    internal static class CalendarData
    {
        // CalendarData.Invariant.iCurrentEra
        internal static int GetCalendarCurrentEra(Calendar calendar) => 1;

        // CalendarData.Invariant.iTwoDigitYearMax, as CalendarData.Unix returns in invariant mode.
        internal static int GetTwoDigitYearMax(CalendarId calendarId) => 2049;
    }
}
