// From the compiler harness's InvariantDateParsingCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;
namespace CategoryFixtureInvariantDateParsing;
public static class EntryPoint
{
    public static int Run(int input)
    {
        string[] dates = { "0001-01-01", "9999-12-31", "2024-02-29", "2026-01-02" };
        DateTime[] expected = { DateTime.MinValue, new DateTime(9999, 12, 31), new DateTime(2024, 2, 29), new DateTime(2026, 1, 2) };
        for (var i = 0; i < dates.Length; i++)
        {
            if (!DateTime.TryParse(dates[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) || value != expected[i] || value.Kind != DateTimeKind.Unspecified) return 10 + i;
            if (DateTime.Parse(dates[i], CultureInfo.InvariantCulture) != value || DateTime.Parse(dates[i].AsSpan(), CultureInfo.InvariantCulture) != value) return 20 + i;
        }
        var clock = new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234567);
        if (DateTime.Parse("2026-01-02 03:04:05.1234567", CultureInfo.InvariantCulture) != clock || DateTime.Parse("2026-01-02T03:04:05.1234567", CultureInfo.InvariantCulture) != clock) return 1;
        string[] invalidDates = { "0000-01-01", "2025-02-29", "2026-13-01", "2026-01-32", "2026-01-02X03:04:05", "2026-01-02 25:00:00" };
        foreach (var text in invalidDates)
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ignored)) return 2;
        string[] offsets = { "2026-01-02 +02:00", "2026-01-02 -14:00", "2026-01-02 +14:00", "2026-01-02 03:04:05 +02:00", "2026-01-02T03:04:05-03:00" };
        DateTimeOffset[] offsetExpected = { new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(-14)), new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(14)), new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-3)) };
        for (var i = 0; i < offsets.Length; i++)
        {
            if (!DateTimeOffset.TryParse(offsets[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) || !value.EqualsExact(offsetExpected[i])) return 30 + i;
            if (!DateTimeOffset.Parse(offsets[i], CultureInfo.InvariantCulture).EqualsExact(value)) return 40 + i;
        }
        string[] invalidOffsets = { "2025-02-29 +02:00", "2026-01-02 +14:01", "2026-01-02 +15:00", "2026-01-02 +02:60", "2026-01-02 X02:00", "0001-01-01 +01:00", "9999-12-31 23:59:59 -01:00" };
        foreach (var text in invalidOffsets)
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ignored)) return 3;
        var failed = 0;
        try { DateTime.Parse("2025-02-29", CultureInfo.InvariantCulture); } catch (FormatException) { failed++; }
        try { DateTimeOffset.Parse("2026-01-02 +14:01", CultureInfo.InvariantCulture); } catch (FormatException) { failed++; }
        return failed == 2 ? input + 1 : 4;
    }
}
