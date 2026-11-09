// Portions adapted from dotnet/runtime System.Private.CoreLib DateTimeFormatInfo.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization;

/// <summary>Provides invariant date and time formatting information.</summary>
public sealed class DateTimeFormatInfo : IFormatProvider
{
    private static readonly DateTimeFormatInfo s_invariantInfo = new();

    public static DateTimeFormatInfo InvariantInfo => s_invariantInfo;

    // NetWasm: the invariant format's calendar, which is upstream's invariant one, the
    // Gregorian calendar. The invariant format is read-only, so setting a calendar throws, as
    // upstream's read-only instances do.
    public Calendar Calendar
    {
        get => GregorianCalendar.GetDefaultInstance();
        set => throw new InvalidOperationException(SR.InvalidOperation_ReadOnly);
    }

    public string ShortDatePattern => "MM/dd/yyyy";

    public string LongDatePattern => "dddd, dd MMMM yyyy";

    public string MonthDayPattern => "MMMM dd";

    public string YearMonthPattern => "yyyy MMMM";

    public string ShortTimePattern => "HH:mm";

    public string LongTimePattern => "HH:mm:ss";

    public object? GetFormat(Type? formatType) =>
        formatType == typeof(DateTimeFormatInfo) ? this : null;

    internal static DateTimeFormatInfo GetInstance(IFormatProvider? provider)
    {
        if (provider is null)
        {
            return InvariantInfo;
        }

        if (provider is CultureInfo cultureInfo)
        {
            return cultureInfo.DateTimeFormat;
        }

        return provider.GetFormat(typeof(DateTimeFormatInfo)) as DateTimeFormatInfo ??
            InvariantInfo;
    }
}
