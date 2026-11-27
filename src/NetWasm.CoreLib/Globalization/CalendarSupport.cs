// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm support for the calendars ported from dotnet/runtime commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce. Upstream calendars call internal DateTime and
// DateOnly members and one LocalAppContextSwitches value that NetWasm's types do not declare;
// these give the same results from NetWasm's own date arithmetic.

using System.Runtime.CompilerServices;

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
    }
}

namespace System
{
    internal static partial class LocalAppContextSwitches
    {
        // Upstream caches the switch; the calendars read it only on era-range edge cases.
        internal static bool EnforceJapaneseEraYearRanges
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                AppContext.TryGetSwitch("Switch.System.Globalization.EnforceJapaneseEraYearRanges", out bool enabled) && enabled;
        }

        // DateTimeFormat and DateTimeParse read these only for a Japanese calendar.
        internal static bool FormatJapaneseFirstYearAsANumber
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                AppContext.TryGetSwitch("Switch.System.Globalization.FormatJapaneseFirstYearAsANumber", out bool enabled) && enabled;
        }

        internal static bool EnforceLegacyJapaneseDateParsing
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                AppContext.TryGetSwitch("Switch.System.Globalization.EnforceLegacyJapaneseDateParsing", out bool enabled) && enabled;
        }

        // Encoding.UTF7 and GetEncoding(65000) read it, as upstream's Encoding and EncodingTable do.
        internal static bool EnableUnsafeUTF7Encoding
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                AppContext.TryGetSwitch("System.Text.Encoding.EnableUnsafeUTF7Encoding", out bool enabled) && enabled;
        }
    }
}
