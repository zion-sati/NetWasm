// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization
{
    // NetWasm: the parameterless ToString of DateTime, DateTimeOffset, DateOnly and TimeOnly while no current culture
    // is set. NetWasm's current DateTimeFormatInfo is then the invariant one, for which upstream's DateTimeFormat
    // writes the invariant general format ("MM/dd/yyyy HH:mm:ss", with " +hh:mm" for an offset), and whose short date
    // ("d", DateOnly's default) and short time ("t", TimeOnly's) are that format's date and time. Each ToString tests
    // DateTimeFormatInfo.s_currentInfo itself, so a program that never sets a culture has the load folded to null and
    // the branch to DateTimeFormat removed: the culture formatter, the local time zone and the clock stay unreached,
    // though a boxed date's ToString is reachable by virtual dispatch.
    internal static class InvariantDateTimeDefaults
    {
        private const int GeneralLength = 19, GeneralWithOffsetLength = 26, ShortDateLength = 10, ShortTimeStart = 11, ShortTimeLength = 5;

        internal static string General(DateTime value)
        {
            Span<char> destination = stackalloc char[GeneralLength];
            DateTimeFormat.TryFormatInvariantG(value, new TimeSpan(DateTimeFormat.NullOffset), destination, out int written);
            return new string(destination.Slice(0, written));
        }

        internal static string General(DateTime clock, TimeSpan offset)
        {
            Span<char> destination = stackalloc char[GeneralWithOffsetLength];
            DateTimeFormat.TryFormatInvariantG(clock, offset, destination, out int written);
            return new string(destination.Slice(0, written));
        }

        internal static string ShortDate(DateTime value)
        {
            Span<char> destination = stackalloc char[GeneralLength];
            DateTimeFormat.TryFormatInvariantG(value, new TimeSpan(DateTimeFormat.NullOffset), destination, out _);
            return new string(destination.Slice(0, ShortDateLength));
        }

        internal static string ShortTime(DateTime value)
        {
            Span<char> destination = stackalloc char[GeneralLength];
            DateTimeFormat.TryFormatInvariantG(value, new TimeSpan(DateTimeFormat.NullOffset), destination, out _);
            return new string(destination.Slice(ShortTimeStart, ShortTimeLength));
        }
    }
}
