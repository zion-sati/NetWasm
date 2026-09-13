// Adapted from dotnet/runtime System.Private.CoreLib DateTime.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    internal static class GregorianDateMath
    {
        internal const int DaysTo1970 = 719162;
        internal const int DaysTo10000 = 3652059;
        internal const long UnixEpochTicks =
            (long)DaysTo1970 * TimeSpan.TicksPerDay;
        private static readonly int[] s_daysToMonth365 =
            [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365];
        private static readonly int[] s_daysToMonth366 =
            [0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366];

        internal static bool IsLeapYear(int year)
        {
            if ((uint)(year - 1) >= 9999)
            {
                throw new ArgumentOutOfRangeException();
            }
            return (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0);
        }

        internal static int DateToDayNumber(int year, int month, int day)
        {
            if ((uint)(year - 1) >= 9999 || (uint)(month - 1) >= 12)
            {
                throw new ArgumentOutOfRangeException();
            }
            var days = IsLeapYear(year) ? s_daysToMonth366 : s_daysToMonth365;
            var daysInMonth = days[month] - days[month - 1];
            if ((uint)(day - 1) >= (uint)daysInMonth)
            {
                throw new ArgumentOutOfRangeException();
            }
            var previousYear = year - 1;
            return previousYear * 365 + previousYear / 4 - previousYear / 100
                + previousYear / 400 + days[month - 1] + day - 1;
        }

        internal static void GetDateParts(
            int dayNumber,
            out int year,
            out int month,
            out int day)
        {
            if ((uint)dayNumber >= DaysTo10000)
            {
                throw new ArgumentOutOfRangeException();
            }
            var remaining = dayNumber;
            var years400 = remaining / 146097;
            remaining -= years400 * 146097;
            var years100 = remaining / 36524;
            if (years100 == 4)
            {
                years100 = 3;
            }
            remaining -= years100 * 36524;
            var years4 = remaining / 1461;
            remaining -= years4 * 1461;
            var years1 = remaining / 365;
            if (years1 == 4)
            {
                years1 = 3;
            }
            remaining -= years1 * 365;
            year = years400 * 400 + years100 * 100 + years4 * 4 + years1 + 1;
            var days = IsLeapYear(year) ? s_daysToMonth366 : s_daysToMonth365;
            month = (remaining >> 5) + 1;
            while (remaining >= days[month])
            {
                month++;
            }
            day = remaining - days[month - 1] + 1;
        }
    }
}
