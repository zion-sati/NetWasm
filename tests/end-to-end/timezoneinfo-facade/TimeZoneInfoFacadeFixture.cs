using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace NetWasm.Tests.TimeZoneInfoFacade.Fixture;

public static class EntryPoint
{
    public static int Run(int input) => input switch
    {
        0 => UtcIdentity(),
        1 => UtcLookup(),
        2 => MelbourneOffsets(),
        3 => NewYorkOffsets(),
        4 => MelbourneTransitionBoundaries(),
        5 => NewYorkTransitionBoundaries(),
        6 => ConversionRoundTrip(),
        7 => HistoricalOffset(),
        8 => ZoneEquality(),
        9 => InvalidIdentifier(),
        10 => LocalZoneIsUsable(),
        11 => SelectedZoneCatalog(),
        _ => -100,
    };

    public static int RunLocalSemantics(int input) => input switch
    {
        0 => AmbiguousLocalRoundTripAndLayout(),
        1 => SourceKindValidation(),
        2 => ConversionBounds(),
        3 => LocalProjectionAndFileTime(),
        4 => ExceptionContracts(),
        5 => TimeSpanStandardFormats(),
        6 => DateTimeStandardFormats(),
        7 => IanaIdentityAndCase(),
        8 => CustomInvariantFormatting(),
        9 => DateTimeStyleSemantics(),
        10 => AdjustmentRuleShapeAndUtcLocalIdentity(),
        _ => -2000,
    };

    // These entry points are run by the deployment-negative tests with the
    // corresponding asset deliberately absent or corrupted. They intentionally
    // leave the managed exception uncaught so the host can assert the public
    // failure contract.
    public static int RunMissingAsset(int input)
    {
        _ = input;
        return TimeZoneInfo.Local
            .GetUtcOffset(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc))
            .Hours;
    }

    public static int RunCorruptAsset(int input)
    {
        _ = input;
        return TimeZoneInfo.Local
            .GetUtcOffset(new DateTime(2024, 7, 15, 0, 0, 0, DateTimeKind.Utc))
            .Hours;
    }

    private static int UtcIdentity()
    {
        var utc = TimeZoneInfo.Utc;
        return utc.Id == "UTC" &&
            utc.BaseUtcOffset == TimeSpan.Zero &&
            !utc.SupportsDaylightSavingTime
            ? 101
            : -101;
    }

    private static int UtcLookup()
    {
        var utc = TimeZoneInfo.FindSystemTimeZoneById("UTC");
        return utc.Equals(TimeZoneInfo.Utc) &&
            utc.GetUtcOffset(DateTime.UnixEpoch) == TimeSpan.Zero
            ? 201
            : -201;
    }

    private static int MelbourneOffsets()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var summer = zone.GetUtcOffset(Utc(2024, 1, 15));
        var winter = zone.GetUtcOffset(Utc(2024, 7, 15));
        return zone.BaseUtcOffset == TimeSpan.FromHours(10) &&
            summer == TimeSpan.FromHours(11) &&
            winter == TimeSpan.FromHours(10) &&
            zone.SupportsDaylightSavingTime
            ? 301
            : -301;
    }

    private static int NewYorkOffsets()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var winter = zone.GetUtcOffset(Utc(2024, 1, 15));
        var summer = zone.GetUtcOffset(Utc(2024, 7, 15));
        return zone.BaseUtcOffset == TimeSpan.FromHours(-5) &&
            winter == TimeSpan.FromHours(-5) &&
            summer == TimeSpan.FromHours(-4) &&
            zone.SupportsDaylightSavingTime
            ? 401
            : -401;
    }

    private static int MelbourneTransitionBoundaries()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var invalid = new DateTime(
            2024, 10, 6, 2, 30, 0, DateTimeKind.Unspecified);
        var ambiguous = new DateTime(
            2024, 4, 7, 2, 30, 0, DateTimeKind.Unspecified);
        return zone.IsInvalidTime(invalid) && zone.IsAmbiguousTime(ambiguous)
            ? 501
            : -501;
    }

    private static int NewYorkTransitionBoundaries()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var invalid = new DateTime(
            2024, 3, 10, 2, 30, 0, DateTimeKind.Unspecified);
        var ambiguous = new DateTime(
            2024, 11, 3, 1, 30, 0, DateTimeKind.Unspecified);
        return zone.IsInvalidTime(invalid) && zone.IsAmbiguousTime(ambiguous)
            ? 601
            : -601;
    }

    private static int ConversionRoundTrip()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var utc = Utc(2024, 7, 15, 12);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var roundTrip = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return local.Hour == 8 &&
            local.Kind == DateTimeKind.Unspecified &&
            roundTrip == utc
            ? 701
            : -701;
    }

    private static int HistoricalOffset()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var historical = zone.GetUtcOffset(Utc(1900, 1, 15));
        return historical == TimeSpan.FromHours(-5) ? 801 : -801;
    }

    private static int ZoneEquality()
    {
        var first = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var second = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        return first.Equals(second) &&
            first.GetHashCode() == second.GetHashCode() &&
            first.Id == second.Id
            ? 901
            : -901;
    }

    private static int InvalidIdentifier()
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById("NetWasm/T01/Absent");
            return -1001;
        }
        catch (TimeZoneNotFoundException)
        {
            return 1001;
        }
    }

    private static int LocalZoneIsUsable()
    {
        var local = TimeZoneInfo.Local;
        var offset = local.GetUtcOffset(Utc(2024, 7, 15));
        return !string.IsNullOrEmpty(local.Id) &&
            offset >= TimeSpan.FromHours(-14) &&
            offset <= TimeSpan.FromHours(14)
            ? 1101
            : -1101;
    }

    private static int SelectedZoneCatalog()
    {
        var zones = TimeZoneInfo.GetSystemTimeZones();
        var foundMelbourne = false;
        var foundNewYork = false;
        foreach (var zone in zones)
        {
            foundMelbourne |= zone.Id == "Australia/Melbourne";
            foundNewYork |= zone.Id == "America/New_York";
        }

        return foundMelbourne && foundNewYork ? 1201 : -1201;
    }

    private static int AmbiguousLocalRoundTripAndLayout()
    {
        var daylightUtc = Utc(2024, 4, 6, 15).AddMinutes(30);
        var standardUtc = Utc(2024, 4, 6, 16).AddMinutes(30);
        var daylightLocal = daylightUtc.ToLocalTime();
        var standardLocal = standardUtc.ToLocalTime();
        return Unsafe.SizeOf<DateTime>() == 8 &&
            daylightLocal == standardLocal &&
            daylightLocal.Kind == DateTimeKind.Local &&
            daylightLocal.Hour == 2 &&
            daylightLocal.Minute == 30 &&
            daylightLocal.IsDaylightSavingTime() &&
            !standardLocal.IsDaylightSavingTime() &&
            daylightLocal.ToUniversalTime() == daylightUtc &&
            standardLocal.ToUniversalTime() == standardUtc
            ? 1301
            : -1301;
    }

    private static int SourceKindValidation()
    {
        var melbourne = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        try
        {
            _ = TimeZoneInfo.ConvertTime(Utc(2024, 1, 1), melbourne, TimeZoneInfo.Utc);
            return -1401;
        }
        catch (ArgumentException)
        {
            return 1401;
        }
    }

    private static int ConversionBounds()
    {
        var melbourne = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var belowMinimum = TimeZoneInfo.ConvertTime(
            DateTime.MinValue,
            melbourne,
            TimeZoneInfo.Utc);
        var aboveMaximum = TimeZoneInfo.ConvertTime(
            DateTime.MaxValue,
            newYork,
            TimeZoneInfo.Utc);
        return belowMinimum == DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) &&
            aboveMaximum == DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
            ? 1501
            : -1501;
    }

    private static int LocalProjectionAndFileTime()
    {
        var foreignOffset = new DateTimeOffset(
            new DateTime(2024, 1, 15, 12, 0, 0),
            TimeSpan.FromHours(1));
        var projected = foreignOffset.LocalDateTime;
        var daylightUtc = Utc(2024, 4, 6, 15).AddMinutes(30);
        var daylightLocal = daylightUtc.ToLocalTime();
        var fileTime = daylightLocal.ToFileTime();
        var fileEpoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return projected.Kind == DateTimeKind.Local &&
            projected.Hour == 22 &&
            projected.ToUniversalTime() == foreignOffset.UtcDateTime &&
            DateTime.FromFileTimeUtc(0) == fileEpoch &&
            DateTime.FromFileTime(fileTime).ToUniversalTime() == daylightUtc &&
            DateTimeOffset.FromFileTime(fileTime).UtcDateTime == daylightUtc &&
            foreignOffset.ToFileTime() == foreignOffset.UtcDateTime.ToFileTimeUtc()
            ? 1601
            : -1601;
    }

    private static int ExceptionContracts()
    {
        var failures = 0;
        try
        {
            _ = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromTicks(1));
        }
        catch (ArgumentException exception)
            when (exception is not ArgumentOutOfRangeException)
        {
            failures++;
        }
        try
        {
            _ = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromHours(15));
        }
        catch (ArgumentOutOfRangeException)
        {
            failures++;
        }
        try
        {
            _ = DateTime.MinValue.Subtract(TimeSpan.MaxValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            failures++;
        }
        try
        {
            _ = DateTime.MaxValue.Subtract(TimeSpan.MinValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            failures++;
        }
        return failures == 4 ? 1701 : -1701;
    }

    private static int TimeSpanStandardFormats()
    {
        var value = TimeSpan.FromTicks(
            TimeSpan.TicksPerDay +
            2 * TimeSpan.TicksPerHour +
            3 * TimeSpan.TicksPerMinute +
            4 * TimeSpan.TicksPerSecond +
            1_200_000);
        return value.ToString("c") == "1.02:03:04.1200000" &&
            value.ToString("g") == "1:2:03:04.12" &&
            value.ToString("G") == "1:02:03:04.1200000" &&
            TimeSpan.Zero.ToString("G") == "0:00:00:00.0000000" &&
            TimeSpan.ParseExact("1.02:03:04.1200000", "c", null) == value &&
            TimeSpan.ParseExact("1:2:03:04.12", "g", null) == value &&
            TimeSpan.ParseExact("1:02:03:04.1200000", "G", null) == value &&
            !TimeSpan.TryParseExact("1:2:03:04.12", "c", null, out _) &&
            !TimeSpan.TryParseExact("1:02:03:04", "G", null, out _)
            ? 1801
            : -1801;
    }

    private static int DateTimeStandardFormats()
    {
        var value = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified)
            .AddTicks(1_200_000);
        var localClock = new DateTime(2024, 1, 15, 7, 8, 9);
        var offset = new DateTimeOffset(localClock, TimeSpan.FromHours(11));
        return value.ToString() == "05/06/2024 07:08:09" &&
            value.ToString("G") == "05/06/2024 07:08:09" &&
            value.ToString("g") == "05/06/2024 07:08" &&
            value.ToString("s") == "2024-05-06T07:08:09" &&
            value.ToString("u") == "2024-05-06 07:08:09Z" &&
            value.ToString("O") == "2024-05-06T07:08:09.1200000" &&
            DateTime.ParseExact("05/06/2024 07:08:09", "G", null) ==
                new DateTime(2024, 5, 6, 7, 8, 9) &&
            DateTime.ParseExact("05/06/2024 07:08", "g", null) ==
                new DateTime(2024, 5, 6, 7, 8, 0) &&
            DateTime.ParseExact("2024-05-06T07:08:09", "s", null) ==
                new DateTime(2024, 5, 6, 7, 8, 9) &&
            DateTime.ParseExact("2024-05-06 07:08:09Z", "u", null).Kind ==
                DateTimeKind.Unspecified &&
            DateTime.ParseExact(value.ToString("O"), "O", null) == value &&
            !DateTime.TryParseExact(
                "2024-05-06T07:08:09.1",
                "s",
                null,
                DateTimeStyles.None,
                out _) &&
            offset.ToString() == "01/15/2024 07:08:09 +11:00" &&
            offset.ToString("G") == "01/15/2024 07:08:09" &&
            offset.ToString("g") == "01/15/2024 07:08" &&
            DateTimeOffset.Parse(offset.ToString()) == offset &&
            DateTimeOffset.ParseExact(offset.ToString("O"), "O", null) == offset &&
            DateTimeOffset.ParseExact(offset.ToString("G"), "G", null) == offset
            ? 1901
            : -1901;
    }

    private static int IanaIdentityAndCase()
    {
        var rejectedBeforeCache = false;
        var rejectedAfterCache = false;
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById("australia/melbourne");
        }
        catch (TimeZoneNotFoundException)
        {
            rejectedBeforeCache = true;
        }
        var exact = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById("australia/melbourne");
        }
        catch (TimeZoneNotFoundException)
        {
            rejectedAfterCache = true;
        }
        return rejectedBeforeCache &&
            rejectedAfterCache &&
            exact.HasIanaId &&
            TimeZoneInfo.Utc.HasIanaId
            ? 2001
            : -2001;
    }

    private static int CustomInvariantFormatting()
    {
        var value = new DateTime(2024, 5, 6, 19, 8, 9, DateTimeKind.Utc)
            .AddTicks(1_234_000);
        var offset = new DateTimeOffset(
            new DateTime(2024, 5, 6, 19, 8, 9),
            TimeSpan.FromHours(11));
        if (value.ToString("dddd, dd MMMM yyyy HH:mm:ss.fffffff tt K") !=
                "Monday, 06 May 2024 19:08:09.1234000 PM Z" ||
            value.ToString("yyyyMMdd'T'HHmmss") != "20240506T190809" ||
            value.ToString("M/d/yy h:mm tt") != "5/6/24 7:08 PM" ||
            offset.ToString("yyyy-MM-dd HH:mm:ss zzz K") !=
                "2024-05-06 19:08:09 +11:00 +11:00")
        {
            return -2101;
        }
        if (!DateTime.TryParseExact(
                "Monday, 06 May 2024 07:08:09.1234000 PM Z",
                "dddd, dd MMMM yyyy hh:mm:ss.fffffff tt K",
                null,
                DateTimeStyles.RoundtripKind,
                out var parsed) || parsed != value)
        {
            return -2102;
        }
        if (!DateTime.TryParseExact(
                "5/6/24 7:08 PM",
                "M/d/yy h:mm tt",
                null,
                DateTimeStyles.None,
                out parsed) || parsed != new DateTime(2024, 5, 6, 19, 8, 0))
        {
            return -2103;
        }
        if (!DateTimeOffset.TryParseExact(
                "2024-05-06 19:08:09 +11:00",
                "yyyy-MM-dd HH:mm:ss zzz",
                null,
                DateTimeStyles.None,
                out var parsedOffset) || parsedOffset != offset)
        {
            return -2104;
        }
        if (DateTime.MinValue.ToString("HH:mm:ss.FFFFFFF") != "00:00:00" ||
            // A time without a date takes today's date unless NoCurrentDateDefault, as on .NET 11.
            !DateTime.TryParseExact(
                "00:00:00",
                "HH:mm:ss.FFFFFFF",
                null,
                DateTimeStyles.NoCurrentDateDefault,
                out parsed) || parsed != DateTime.MinValue)
        {
            return -2105;
        }
        return !DateTime.TryParseExact(
                "Tuesday, 06 May 2024",
                "dddd, dd MMMM yyyy",
                null,
                DateTimeStyles.None,
                out _)
            ? 2101
            : -2106;
    }

    private static int DateTimeStyleSemantics()
    {
        const string sortable = "2024-01-15T07:08:09";
        if (!DateTime.TryParseExact(
                "  " + sortable,
                "s",
                null,
                DateTimeStyles.AllowLeadingWhite,
                out var leading) ||
            leading != new DateTime(2024, 1, 15, 7, 8, 9) ||
            DateTime.TryParseExact(
                sortable + "  ",
                "s",
                null,
                DateTimeStyles.AllowLeadingWhite,
                out _))
        {
            return -2201;
        }
        if (!DateTime.TryParseExact(
                sortable,
                "s",
                null,
                DateTimeStyles.AssumeUniversal,
                out var assumedUtc) ||
            assumedUtc.Kind != DateTimeKind.Local ||
            assumedUtc.Hour != 18 ||
            assumedUtc.ToUniversalTime() !=
                new DateTime(2024, 1, 15, 7, 8, 9, DateTimeKind.Utc))
        {
            return -2202;
        }
        if (!DateTime.TryParseExact(
                sortable,
                "s",
                null,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var adjustedUtc) ||
            adjustedUtc.Kind != DateTimeKind.Utc ||
            adjustedUtc.Hour != 7)
        {
            return -2203;
        }
        if (!DateTime.TryParseExact(
                sortable,
                "s",
                null,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal,
                out var adjustedLocal) ||
            adjustedLocal.Kind != DateTimeKind.Utc ||
            adjustedLocal.Hour != 20 ||
            adjustedLocal.Day != 14)
        {
            return -2204;
        }
        if (!DateTimeOffset.TryParseExact(
                "05/06/2024 07:08:09",
                "G",
                null,
                DateTimeStyles.AssumeUniversal,
                out var universalOffset) ||
            universalOffset.Offset != TimeSpan.Zero)
        {
            return -2205;
        }
        try
        {
            _ = DateTime.ParseExact(
                sortable,
                "s",
                null,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal);
            return -2206;
        }
        catch (ArgumentException)
        {
            return 2201;
        }
    }

    private static int AdjustmentRuleShapeAndUtcLocalIdentity()
    {
        var local = TimeZoneInfo.Local;
        if (local.Id != "Australia/Melbourne" || !local.HasIanaId)
        {
            return -2301;
        }
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById("Local");
            return -2302;
        }
        catch (TimeZoneNotFoundException)
        {
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var rules = zone.GetAdjustmentRules();
        if (rules.Length < 3 || rules[0].DateStart != DateTime.MinValue ||
            rules[^1].DateEnd != DateTime.MaxValue)
        {
            return -2303;
        }
        var foundDaylight = false;
        var foundStandard = false;
        for (var index = 0; index < rules.Length; index++)
        {
            var rule = rules[index];
            if (index > 0 && rule.DateStart.Kind != DateTimeKind.Utc ||
                index < rules.Length - 1 && rule.DateEnd.Kind != DateTimeKind.Utc ||
                index != 0 && rule.DateStart.Ticks != rules[index - 1].DateEnd.Ticks + 1)
            {
                return -2304;
            }
            foundDaylight |= rule.DaylightDelta == TimeSpan.FromHours(1);
            foundStandard |= rule.BaseUtcOffsetDelta == TimeSpan.Zero &&
                rule.DaylightDelta == TimeSpan.Zero;
        }
        rules[0] = null!;
        return foundDaylight && foundStandard &&
            zone.GetAdjustmentRules()[0] != null
            ? 2301
            : -2305;
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
