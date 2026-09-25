using System;

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

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
