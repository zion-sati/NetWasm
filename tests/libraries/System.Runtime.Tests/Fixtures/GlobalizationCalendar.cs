// From the compiler harness's GlobalizationCalendarCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;
namespace GlobalizationCalendarFixture;

// A caller-defined calendar exercises Calendar's own virtual defaults.
sealed class Shifted : Calendar
{
    private readonly GregorianCalendar _inner = new();
    public override int[] Eras => new[] { 1 };
    public override DateTime AddMonths(DateTime time, int months) => _inner.AddMonths(time, months);
    public override DateTime AddYears(DateTime time, int years) => _inner.AddYears(time, years);
    public override int GetDayOfMonth(DateTime time) => _inner.GetDayOfMonth(time);
    public override DayOfWeek GetDayOfWeek(DateTime time) => _inner.GetDayOfWeek(time);
    public override int GetDayOfYear(DateTime time) => _inner.GetDayOfYear(time);
    public override int GetDaysInMonth(int year, int month, int era) => _inner.GetDaysInMonth(year, month, era);
    public override int GetDaysInYear(int year, int era) => _inner.GetDaysInYear(year, era);
    public override int GetEra(DateTime time) => 1;
    public override int GetMonth(DateTime time) => _inner.GetMonth(time);
    public override int GetMonthsInYear(int year, int era) => 12;
    public override int GetYear(DateTime time) => _inner.GetYear(time);
    public override bool IsLeapDay(int year, int month, int day, int era) => _inner.IsLeapDay(year, month, day, era);
    public override bool IsLeapMonth(int year, int month, int era) => false;
    public override bool IsLeapYear(int year, int era) => _inner.IsLeapYear(year, era);
    public override DateTime ToDateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, int era) =>
        _inner.ToDateTime(year, month, day, hour, minute, second, millisecond, era);
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // Gregorian arithmetic, leap rules and boundaries.
        var g = new GregorianCalendar();
        if (g.ToDateTime(2024, 2, 29, 13, 14, 15, 16) != new DateTime(2024, 2, 29, 13, 14, 15, 16)) return 1;
        if (!g.IsLeapYear(2024) || g.IsLeapYear(2100) || !g.IsLeapYear(2000) || g.GetDaysInMonth(2023, 2) != 28 || g.GetDaysInYear(2024) != 366) return 2;
        if (g.AddMonths(new DateTime(2024, 1, 31), 1) != new DateTime(2024, 2, 29) || g.AddYears(new DateTime(2024, 2, 29), 1) != new DateTime(2025, 2, 28)) return 3;
        if (g.GetDayOfWeek(new DateTime(2024, 10, 20)) != DayOfWeek.Sunday || g.GetDayOfYear(new DateTime(2024, 12, 31)) != 366 || !g.IsLeapDay(2024, 2, 29)) return 4;
        if (g.Eras.Length != 1 || g.Eras[0] != 1 || g.GetEra(new DateTime(2024, 1, 1)) != 1 || g.CalendarType != GregorianCalendarTypes.Localized) return 5;
        if (g.TwoDigitYearMax != 2049 || g.ToFourDigitYear(49) != 2049 || g.ToFourDigitYear(50) != 1950 || g.MinSupportedDateTime != DateTime.MinValue || g.MaxSupportedDateTime != DateTime.MaxValue) return 6;
        if (g.GetWeekOfYear(new DateTime(2024, 1, 1), CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday) != 1 ||
            g.GetWeekOfYear(new DateTime(2021, 1, 1), CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday) != 53 ||
            g.GetWeekOfYear(new DateTime(2024, 12, 31), CalendarWeekRule.FirstDay, DayOfWeek.Sunday) != 53) return 7;
        if (!Throws<ArgumentOutOfRangeException>(() => g.ToDateTime(2023, 2, 29, 0, 0, 0, 0)) ||
            !Throws<ArgumentOutOfRangeException>(() => new GregorianCalendar((GregorianCalendarTypes)99)) ||
            !Throws<ArgumentException>(() => g.AddMonths(DateTime.MaxValue, 1)) ||
            !Throws<ArgumentOutOfRangeException>(() => g.TwoDigitYearMax = 10000)) return 8;
        if (g.AlgorithmType != CalendarAlgorithmType.SolarCalendar || new GregorianCalendar(GregorianCalendarTypes.USEnglish).CalendarType != GregorianCalendarTypes.USEnglish) return 9;

        // Read-only wrappers and clones.
        var frozen = Calendar.ReadOnly(g);
        var copy = (Calendar)frozen.Clone();
        if (!frozen.IsReadOnly || g.IsReadOnly || copy.IsReadOnly || !Throws<InvalidOperationException>(() => frozen.TwoDigitYearMax = 2050)) return 10;
        copy.TwoDigitYearMax = 2060;
        if (copy.TwoDigitYearMax != 2060 || frozen.TwoDigitYearMax != 2049 || !Throws<ArgumentNullException>(() => Calendar.ReadOnly(null!))) return 11;

        // Calendar's virtual defaults through a caller-defined calendar.
        var shifted = new Shifted();
        var origin = new DateTime(2024, 3, 1, 10, 0, 0);
        if (shifted.AddWeeks(origin, 2) != origin.AddDays(14) || shifted.AddDays(origin, -1) != origin.AddDays(-1) ||
            shifted.AddHours(origin, 25) != origin.AddHours(25) || shifted.AddMilliseconds(origin, 1500) != origin.AddMilliseconds(1500)) return 12;
        if (shifted.GetHour(origin) != 10 || shifted.GetMinute(origin) != 0 || shifted.GetDaysInMonth(2024, 2) != 29 || !shifted.IsLeapYear(2024) ||
            shifted.ToDateTime(2024, 3, 1, 10, 0, 0, 0) != origin || shifted.TwoDigitYearMax != -1) return 13;
        shifted.TwoDigitYearMax = 2029;
        if (shifted.ToFourDigitYear(24) != 2024 || shifted.ToFourDigitYear(30) != 1930 || shifted.ToFourDigitYear(150) != 150 ||
            !Throws<ArgumentOutOfRangeException>(() => shifted.ToFourDigitYear(-1))) return 31;
        if (shifted.GetWeekOfYear(origin, CalendarWeekRule.FirstFullWeek, DayOfWeek.Monday) != 9 || shifted.AlgorithmType != CalendarAlgorithmType.Unknown) return 14;

        // Julian.
        var j = new JulianCalendar();
        if (j.ToDateTime(2024, 1, 1, 0, 0, 0, 0) != new DateTime(2024, 1, 14) || !j.IsLeapYear(1900) || j.GetYear(new DateTime(2024, 1, 14)) != 2024 ||
            j.GetMonth(new DateTime(2024, 1, 14)) != 1 || j.GetDayOfMonth(new DateTime(2024, 1, 14)) != 1 || j.AddMonths(new DateTime(2024, 1, 14), 1) != new DateTime(2024, 2, 14)) return 15;

        // Hebrew: Rosh Hashana 5785 fell on 3 October 2024; 5784 was a leap year.
        var h = new HebrewCalendar();
        var rosh = new DateTime(2024, 10, 3);
        if (h.GetYear(rosh) != 5785 || h.GetMonth(rosh) != 1 || h.GetDayOfMonth(rosh) != 1 || !h.IsLeapYear(5784) || h.GetMonthsInYear(5784) != 13 || h.GetMonthsInYear(5785) != 12) return 16;
        if (h.ToDateTime(5785, 1, 1, 0, 0, 0, 0) != rosh || h.GetYear(h.MinSupportedDateTime) != 5343 || h.GetYear(h.MaxSupportedDateTime) != 5999 ||
            !Throws<ArgumentOutOfRangeException>(() => h.ToDateTime(6000, 1, 1, 0, 0, 0, 0)) || h.AlgorithmType != CalendarAlgorithmType.LunisolarCalendar) return 17;

        // Hijri (tabular) and Um Al Qura round trips, leap years and ranges.
        var hijri = new HijriCalendar();
        var hijriDate = hijri.ToDateTime(1446, 1, 1, 0, 0, 0, 0);
        if (hijri.HijriAdjustment != 0 || hijri.GetYear(hijriDate) != 1446 || hijri.GetMonth(hijriDate) != 1 || hijri.GetDayOfMonth(hijriDate) != 1 ||
            !hijri.IsLeapYear(1445) || hijri.GetDaysInYear(1445) != 355 || hijri.GetDaysInYear(1446) != 354 || hijri.AlgorithmType != CalendarAlgorithmType.LunarCalendar) return 18;
        var umm = new UmAlQuraCalendar();
        var ummDate = umm.ToDateTime(1446, 9, 1, 0, 0, 0, 0);
        if (umm.GetYear(ummDate) != 1446 || umm.GetMonth(ummDate) != 9 || umm.GetDayOfMonth(ummDate) != 1 || umm.MinSupportedDateTime != new DateTime(1900, 4, 30) ||
            !Throws<ArgumentOutOfRangeException>(() => umm.GetYear(new DateTime(1800, 1, 1)))) return 19;

        // Persian: Nowruz 1403 fell on 20 March 2024, a leap year.
        var p = new PersianCalendar();
        var nowruz = new DateTime(2024, 3, 20);
        if (p.GetYear(nowruz) != 1403 || p.GetMonth(nowruz) != 1 || p.GetDayOfMonth(nowruz) != 1 || !p.IsLeapYear(1403) || p.IsLeapYear(1404) ||
            p.ToDateTime(1403, 1, 1, 0, 0, 0, 0) != nowruz || p.GetDaysInMonth(1403, 12) != 30) return 20;

        // Thai Buddhist years run 543 ahead of Gregorian.
        var thai = new ThaiBuddhistCalendar();
        if (thai.GetYear(new DateTime(2024, 1, 1)) != 2567 || thai.ToDateTime(2567, 1, 1, 0, 0, 0, 0) != new DateTime(2024, 1, 1) || thai.Eras[0] != 1) return 21;

        // East Asian lunisolar: Chinese New Year 2024 (the Dragon, sexagenary year 41) fell on 10 February.
        var chinese = new ChineseLunisolarCalendar();
        var newYear = new DateTime(2024, 2, 10);
        if (chinese.GetYear(newYear) != 2024 || chinese.GetMonth(newYear) != 1 || chinese.GetDayOfMonth(newYear) != 1 || chinese.GetSexagenaryYear(newYear) != 41 ||
            chinese.GetCelestialStem(41) != 1 || chinese.GetTerrestrialBranch(41) != 5 || chinese.GetLeapMonth(2023) != 3 || chinese.GetMonthsInYear(2023) != 13) return 22;
        if (chinese.ToDateTime(2024, 1, 1, 0, 0, 0, 0) != newYear || new KoreanLunisolarCalendar().GetYear(newYear) != 2024 || new TaiwanLunisolarCalendar().GetYear(newYear) != 113 ||
            !Throws<ArgumentOutOfRangeException>(() => chinese.GetCelestialStem(0))) return 23;

        // Era constants and enum values.
        if (GregorianCalendar.ADEra != 1 || JulianCalendar.JulianEra != 1 || HebrewCalendar.HebrewEra != 1 || HijriCalendar.HijriEra != 1 || UmAlQuraCalendar.UmAlQuraEra != 1 ||
            PersianCalendar.PersianEra != 1 || ThaiBuddhistCalendar.ThaiBuddhistEra != 1 || ChineseLunisolarCalendar.ChineseEra != 1 || KoreanLunisolarCalendar.GregorianEra != 1 ||
            KoreanCalendar.KoreanEra != 1 || JapaneseLunisolarCalendar.JapaneseEra != 1 || Calendar.CurrentEra != 0) return 24;
        if ((int)CalendarAlgorithmType.LunisolarCalendar != 3 || (int)CalendarWeekRule.FirstFourDayWeek != 2 || (int)GregorianCalendarTypes.TransliteratedFrench != 12) return 25;

        // ISO weeks.
        if (ISOWeek.GetWeekOfYear(new DateTime(2021, 1, 3)) != 53 || ISOWeek.GetYear(new DateTime(2021, 1, 3)) != 2020 || ISOWeek.ToDateTime(2020, 53, DayOfWeek.Sunday) != new DateTime(2021, 1, 3)) return 26;
        if (ISOWeek.GetWeeksInYear(2020) != 53 || ISOWeek.GetWeeksInYear(2021) != 52 || ISOWeek.GetYearStart(2021) != new DateTime(2021, 1, 4) || ISOWeek.GetYearEnd(2020) != new DateTime(2021, 1, 3)) return 27;
        if (ISOWeek.GetWeekOfYear(new DateOnly(2021, 1, 3)) != 53 || ISOWeek.GetYear(new DateOnly(2021, 1, 3)) != 2020 || ISOWeek.ToDateOnly(2020, 53, DayOfWeek.Sunday) != new DateOnly(2021, 1, 3)) return 28;
        if (!Throws<ArgumentOutOfRangeException>(() => ISOWeek.ToDateTime(2021, 54, DayOfWeek.Monday)) || !Throws<ArgumentOutOfRangeException>(() => ISOWeek.GetWeeksInYear(0))) return 29;

        // The Japanese lunisolar calendar needs no culture: upstream's built-in eras (Reiwa is the fifth).
        if (new JapaneseLunisolarCalendar().GetEra(new DateTime(2020, 6, 1)) != 5) return 31;
#if !DESKTOP_ORACLE
        // Calendars that need a named culture fail to initialize, as upstream's do in invariant globalization
        // mode with predefined cultures only.
        if (!Throws<TypeInitializationException>(() => new JapaneseCalendar()) || !Throws<TypeInitializationException>(() => new KoreanCalendar()) ||
            !Throws<TypeInitializationException>(() => new TaiwanCalendar())) return 30;
#endif
        return input + 1;
    }
}
