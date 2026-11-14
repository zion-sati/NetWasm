// From the compiler harness's LegacyTimeZoneContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;
namespace LegacyTimeZoneFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var zone = new FixedZone(TimeSpan.FromHours(2));
        var time = new DateTime(2024, 6, 1, 12, 0, 0);
        var utc = zone.ToUniversalTime(time);
        if (utc.Kind != DateTimeKind.Utc || utc.Hour != 10 || zone.ToUniversalTime(utc) != utc) return 1;
        if (zone.ToUniversalTime(DateTime.MinValue).Ticks != DateTime.MinValue.Ticks) return 2;
        if (new FixedZone(TimeSpan.FromHours(-2)).ToUniversalTime(DateTime.MaxValue).Ticks != DateTime.MaxValue.Ticks) return 3;
        var local = DateTime.SpecifyKind(time, DateTimeKind.Local);
        if (zone.ToLocalTime(local) != local || zone.StandardName != "fixed" || zone.DaylightName != "fixed") return 4;
        var changes = zone.GetDaylightChanges(2024);
        if (changes.Start.Month != 3 || changes.End.Month != 10 || changes.Delta != TimeSpan.FromHours(1)) return 5;
        if (!zone.IsDaylightSavingTime(time) || zone.IsDaylightSavingTime(new DateTime(2024, 1, 1))) return 6;
        if (!TimeZone.IsDaylightSavingTime(time, changes) || TimeZone.IsDaylightSavingTime(utc, changes) || TimeZone.IsDaylightSavingTime(time, null)) return 7;
        var southern = new DaylightTime(new DateTime(2024, 10, 1), new DateTime(2024, 3, 1), TimeSpan.FromHours(1));
        if (!TimeZone.IsDaylightSavingTime(new DateTime(2024, 1, 15), southern) || TimeZone.IsDaylightSavingTime(time, southern)) return 8;
        if (TimeZone.IsDaylightSavingTime(new DateTime(2024, 3, 1, 0, 30, 0), changes) || !TimeZone.IsDaylightSavingTime(new DateTime(2024, 3, 1, 1, 0, 0), changes)) return 10;
        if (!TimeZone.IsDaylightSavingTime(new DateTime(2024, 9, 30, 22, 59, 0), changes) || TimeZone.IsDaylightSavingTime(new DateTime(2024, 9, 30, 23, 30, 0), changes)) return 11;
        var negative = new DaylightTime(new DateTime(2024, 10, 1), new DateTime(2024, 3, 1), TimeSpan.FromHours(-1));
        if (TimeZone.IsDaylightSavingTime(new DateTime(2024, 9, 30, 23, 30, 0), negative) || !TimeZone.IsDaylightSavingTime(new DateTime(2024, 10, 1), negative)) return 12;
        if (TimeZone.IsDaylightSavingTime(time, new DaylightTime(changes.Start, changes.End, TimeSpan.Zero))) return 13;
        // Upstream's: the current zone wraps TimeZoneInfo.Local, and the base ToLocalTime converts through it.
        var current = TimeZone.CurrentTimeZone;
        if (!ReferenceEquals(current, TimeZone.CurrentTimeZone) || current.StandardName != TimeZoneInfo.Local.StandardName) return 9;
        if (zone.ToLocalTime(utc) != TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local)) return 14;
        return input + 1;
    }
    private sealed class FixedZone : TimeZone
    {
        private readonly TimeSpan offset;
        public FixedZone(TimeSpan offset) { this.offset = offset; }
        public override string StandardName => "fixed";
        public override string DaylightName => "fixed";
        public override TimeSpan GetUtcOffset(DateTime time) => offset;
        public override DaylightTime GetDaylightChanges(int year) => new DaylightTime(new DateTime(year, 3, 1), new DateTime(year, 10, 1), TimeSpan.FromHours(1));
    }
}
