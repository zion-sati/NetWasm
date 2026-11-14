// From the compiler harness's GlobalizationCultureCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Globalization;
namespace GlobalizationCultureFixture;

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static bool Same(string[] actual, params string[] expected)
    {
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
        return true;
    }

    static bool SameBytes(byte[] actual, params byte[] expected)
    {
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
        return true;
    }

    public static int Run(int input)
    {
        // Invariant culture identity.
        CultureInfo inv = CultureInfo.InvariantCulture;
        if (inv.Name != "" || inv.LCID != 127 || inv.KeyboardLayoutId != 127 || inv.IetfLanguageTag != "" || inv.ToString() != "") return 1;
        if (inv.EnglishName != "Invariant Language (Invariant Country)" || inv.NativeName != inv.EnglishName || inv.DisplayName != inv.EnglishName) return 2;
        if (inv.TwoLetterISOLanguageName != "iv" || inv.ThreeLetterISOLanguageName != "ivl" || inv.ThreeLetterWindowsLanguageName != "IVL") return 3;
        if (inv.IsNeutralCulture || !inv.IsReadOnly || inv.UseUserOverride || inv.CultureTypes != CultureTypes.SpecificCultures || !ReferenceEquals(inv.Parent, inv)) return 4;
        if (inv.Calendar is not GregorianCalendar || !inv.Calendar.IsReadOnly || inv.OptionalCalendars.Length != 1 || inv.OptionalCalendars[0] is not GregorianCalendar) return 5;

        // Named cultures and LCIDs other than the invariant one do not exist in invariant mode.
        if (!Throws<CultureNotFoundException>(() => new CultureInfo("en-US")) || !Throws<CultureNotFoundException>(() => new CultureInfo(1033))) return 6;
        if (!Throws<CultureNotFoundException>(() => CultureInfo.GetCultureInfo("fr")) || !Throws<CultureNotFoundException>(() => CultureInfo.GetCultureInfo(1036))) return 7;
        if (!Throws<ArgumentNullException>(() => new CultureInfo((string)null!)) || !Throws<ArgumentOutOfRangeException>(() => new CultureInfo(-1))) return 8;
        if (new CultureInfo(127).Name != "" || new CultureInfo("", false).UseUserOverride || !CultureInfo.GetCultureInfo("").IsReadOnly || !ReferenceEquals(CultureInfo.GetCultureInfo(""), CultureInfo.GetCultureInfo(""))) return 9;
        CultureInfo[] all = CultureInfo.GetCultures(CultureTypes.AllCultures);
        if (all.Length != 1 || all[0].Name != "" || !Throws<ArgumentOutOfRangeException>(() => CultureInfo.GetCultures(0))) return 10;
        if (CultureInfo.CreateSpecificCulture("").Name != "" || inv.GetConsoleFallbackUICulture().Name != "" || CultureInfo.GetCultureInfoByIetfLanguageTag("").LCID != 127) return 11;
        if (!Throws<CultureNotFoundException>(() => CultureInfo.GetCultureInfoByIetfLanguageTag("zh-CHT"))) return 12;
        if (!inv.Equals(new CultureInfo("")) || inv.GetHashCode() != new CultureInfo("").GetHashCode() || inv.GetFormat(typeof(string)) != null) return 13;

        // Clone and read-only rules.
        var clone = (CultureInfo)inv.Clone();
        if (clone.IsReadOnly || clone.NumberFormat.IsReadOnly || clone.DateTimeFormat.IsReadOnly || clone.TextInfo.IsReadOnly) return 14;
        clone.NumberFormat.NegativeSign = "~";
        clone.NumberFormat.NumberGroupSeparator = "_";
        var frozen = CultureInfo.ReadOnly(clone);
        if (!frozen.IsReadOnly || !frozen.NumberFormat.IsReadOnly || frozen.NumberFormat.NegativeSign != "~" || !ReferenceEquals(CultureInfo.ReadOnly(frozen), frozen)) return 15;
        if (!Throws<InvalidOperationException>(() => frozen.NumberFormat = new NumberFormatInfo()) || !Throws<InvalidOperationException>(() => inv.DateTimeFormat = new DateTimeFormatInfo())) return 16;

        // The current culture's formats drive null-provider formatting.
        if (!ReferenceEquals(CultureInfo.CurrentCulture, inv) || CultureInfo.DefaultThreadCurrentCulture != null || !ReferenceEquals(CultureInfo.InstalledUICulture, inv)) return 17;
        CultureInfo.CurrentCulture = clone;
        string negative = (-5).ToString();
        string grouped = 1234567.ToString("N0");
        bool currentInfo = ReferenceEquals(NumberFormatInfo.CurrentInfo, clone.NumberFormat) && ReferenceEquals(DateTimeFormatInfo.CurrentInfo, clone.DateTimeFormat);
        CultureInfo.CurrentCulture = inv;
        if (negative != "~5" || grouped != "1_234_567" || !currentInfo || (-5).ToString() != "-5") return 18;
        CultureInfo.CurrentUICulture = clone;
        bool ui = ReferenceEquals(CultureInfo.CurrentUICulture, clone);
        CultureInfo.CurrentUICulture = inv;
        if (!ui || !Throws<ArgumentNullException>(() => CultureInfo.CurrentCulture = null!)) return 19;

        // NumberFormatInfo state, validation and providers.
        NumberFormatInfo nfi = NumberFormatInfo.InvariantInfo;
        if (!nfi.IsReadOnly || !ReferenceEquals(nfi, inv.NumberFormat) || !ReferenceEquals(NumberFormatInfo.CurrentInfo, nfi)) return 20;
        if (nfi.CurrencySymbol != "¤" || nfi.PerMilleSymbol != "‰" || nfi.CurrencyDecimalSeparator != "." || nfi.CurrencyGroupSeparator != "," || nfi.PercentDecimalSeparator != "." || nfi.PercentGroupSeparator != ",") return 21;
        if (nfi.NumberGroupSizes.Length != 1 || nfi.NumberGroupSizes[0] != 3 || nfi.CurrencyGroupSizes[0] != 3 || nfi.PercentGroupSizes[0] != 3 || !Same(nfi.NativeDigits, "0", "1", "2", "3", "4", "5", "6", "7", "8", "9")) return 22;
        if (nfi.DigitSubstitution != DigitShapes.None || nfi.NumberNegativePattern != 1 || nfi.CurrencyNegativePattern != 0 || nfi.PercentPositivePattern != 0) return 23;
        var writable = (NumberFormatInfo)nfi.Clone();
        if (writable.IsReadOnly || !Throws<InvalidOperationException>(() => nfi.PositiveSign = "p")) return 24;
        writable.CurrencyGroupSizes = new[] { 3, 2 };
        writable.NumberGroupSizes = new[] { 2, 0 };
        writable.PercentNegativePattern = 11;
        writable.NativeDigits = new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        writable.DigitSubstitution = DigitShapes.NativeNational;
        if (writable.CurrencyGroupSizes[1] != 2 || writable.PercentNegativePattern != 11 || writable.DigitSubstitution != DigitShapes.NativeNational) return 25;
        if (!Throws<ArgumentOutOfRangeException>(() => writable.CurrencyDecimalDigits = 100) || !Throws<ArgumentOutOfRangeException>(() => writable.CurrencyNegativePattern = 17)) return 26;
        if (!Throws<ArgumentException>(() => writable.NumberDecimalSeparator = "") || !Throws<ArgumentException>(() => writable.NumberGroupSizes = new[] { 0, 3 }) || !Throws<ArgumentException>(() => writable.NumberGroupSizes = new[] { 10 })) return 27;
        if (!Throws<ArgumentException>(() => writable.NativeDigits = new[] { "0" }) || !Throws<ArgumentException>(() => writable.DigitSubstitution = (DigitShapes)7)) return 28;
        if (!ReferenceEquals(NumberFormatInfo.GetInstance(writable), writable) || !ReferenceEquals(NumberFormatInfo.GetInstance(inv), nfi) || !ReferenceEquals(NumberFormatInfo.GetInstance(null), nfi) || !NumberFormatInfo.ReadOnly(writable).IsReadOnly) return 29;

        // DateTimeFormatInfo invariant data.
        DateTimeFormatInfo dtfi = DateTimeFormatInfo.InvariantInfo;
        if (!dtfi.IsReadOnly || !ReferenceEquals(dtfi, inv.DateTimeFormat) || !ReferenceEquals(DateTimeFormatInfo.CurrentInfo, dtfi) || dtfi.Calendar is not GregorianCalendar || !dtfi.Calendar.IsReadOnly) return 30;
        if (dtfi.AMDesignator != "AM" || dtfi.PMDesignator != "PM" || dtfi.DateSeparator != "/" || dtfi.TimeSeparator != ":" || dtfi.FirstDayOfWeek != DayOfWeek.Sunday || dtfi.CalendarWeekRule != CalendarWeekRule.FirstDay) return 31;
        if (dtfi.ShortDatePattern != "MM/dd/yyyy" || dtfi.LongDatePattern != "dddd, dd MMMM yyyy" || dtfi.ShortTimePattern != "HH:mm" || dtfi.LongTimePattern != "HH:mm:ss" || dtfi.FullDateTimePattern != "dddd, dd MMMM yyyy HH:mm:ss") return 32;
        if (dtfi.MonthDayPattern != "MMMM dd" || dtfi.YearMonthPattern != "yyyy MMMM" || dtfi.RFC1123Pattern != "ddd, dd MMM yyyy HH':'mm':'ss 'GMT'" || dtfi.SortableDateTimePattern != "yyyy'-'MM'-'dd'T'HH':'mm':'ss" || dtfi.UniversalSortableDateTimePattern != "yyyy'-'MM'-'dd HH':'mm':'ss'Z'") return 33;
        if (dtfi.NativeCalendarName != "Gregorian Calendar" || dtfi.DayNames[1] != "Monday" || dtfi.AbbreviatedDayNames[6] != "Sat" || dtfi.ShortestDayNames[0] != "Su" || dtfi.MonthNames.Length != 13 || dtfi.MonthNames[12] != "") return 34;
        if (dtfi.AbbreviatedMonthNames[0] != "Jan" || dtfi.MonthGenitiveNames[1] != "February" || dtfi.AbbreviatedMonthGenitiveNames[11] != "Dec") return 35;
        if (dtfi.GetDayName(DayOfWeek.Friday) != "Friday" || dtfi.GetAbbreviatedDayName(DayOfWeek.Monday) != "Mon" || dtfi.GetShortestDayName(DayOfWeek.Tuesday) != "Tu" || dtfi.GetMonthName(3) != "March" || dtfi.GetAbbreviatedMonthName(13) != "") return 36;
        if (dtfi.GetEraName(1) != "A.D." || dtfi.GetAbbreviatedEraName(0) != "AD" || dtfi.GetEra("a.d.") != 1 || dtfi.GetEra("AD") != 1 || dtfi.GetEra("BC") != -1 || dtfi.GetEra("") != -1) return 37;
        if (!Throws<ArgumentOutOfRangeException>(() => dtfi.GetMonthName(14)) || !Throws<ArgumentOutOfRangeException>(() => dtfi.GetDayName((DayOfWeek)7)) || !Throws<ArgumentOutOfRangeException>(() => dtfi.GetEraName(2))) return 38;
        if (!Same(dtfi.GetAllDateTimePatterns('d'), "MM/dd/yyyy", "yyyy-MM-dd") || !Same(dtfi.GetAllDateTimePatterns('G'), "MM/dd/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss")) return 39;
        if (!Same(dtfi.GetAllDateTimePatterns('t'), "HH:mm", "hh:mm tt", "H:mm", "h:mm tt") || dtfi.GetAllDateTimePatterns().Length != 34 || !Throws<ArgumentException>(() => dtfi.GetAllDateTimePatterns('x'))) return 40;

        // DateTimeFormatInfo mutation.
        var dt = (DateTimeFormatInfo)dtfi.Clone();
        if (dt.IsReadOnly || dt.Calendar.IsReadOnly || !Throws<InvalidOperationException>(() => dtfi.AMDesignator = "a")) return 41;
        dt.ShortDatePattern = "yyyy/MM/dd";
        dt.LongTimePattern = "HH.mm.ss";
        if (!Same(dt.GetAllDateTimePatterns('d'), "yyyy/MM/dd", "MM/dd/yyyy", "yyyy-MM-dd") || dt.FullDateTimePattern != "dddd, dd MMMM yyyy HH.mm.ss") return 43;
        dt.SetAllDateTimePatterns(new[] { "h", "hh" }, 't');
        if (dt.ShortTimePattern != "h" || !Same(dt.GetAllDateTimePatterns('t'), "h", "hh") || !Throws<ArgumentException>(() => dt.SetAllDateTimePatterns(new[] { "x" }, 'm'))) return 44;
        if (!Throws<ArgumentException>(() => dt.SetAllDateTimePatterns(new string[0], 'd')) || !Throws<ArgumentNullException>(() => dt.SetAllDateTimePatterns(new string[] { null! }, 'd'))) return 45;
        if (!Throws<ArgumentException>(() => dt.DayNames = new[] { "x" }) || !Throws<ArgumentException>(() => dt.MonthNames = new string[12]) || !Throws<ArgumentNullException>(() => dt.AMDesignator = null!)) return 46;
        if (!Throws<ArgumentOutOfRangeException>(() => dt.FirstDayOfWeek = (DayOfWeek)7) || !Throws<ArgumentOutOfRangeException>(() => dt.CalendarWeekRule = (CalendarWeekRule)3) || !Throws<ArgumentOutOfRangeException>(() => dt.Calendar = new JulianCalendar())) return 47;
        dt.Calendar = new GregorianCalendar();
        dt.FirstDayOfWeek = DayOfWeek.Monday;
        dt.MonthNames = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "" };
        if (dt.FirstDayOfWeek != DayOfWeek.Monday || dt.GetMonthName(2) != "2" || dt.ShortDatePattern != "MM/dd/yyyy" || !DateTimeFormatInfo.ReadOnly(dt).IsReadOnly) return 48;
        if (!ReferenceEquals(DateTimeFormatInfo.GetInstance(dt), dt) || !ReferenceEquals(DateTimeFormatInfo.GetInstance(inv), dtfi) || new DateTimeFormatInfo().IsReadOnly) return 49;

        // TextInfo.
        TextInfo ti = inv.TextInfo;
        if (ti.ANSICodePage != 1252 || ti.OEMCodePage != 437 || ti.MacCodePage != 10000 || ti.EBCDICCodePage != 37 || ti.LCID != 127 || ti.IsRightToLeft || ti.CultureName != "" || ti.ListSeparator != ",") return 50;
        if (ti.ToUpper('a') != 'A' || ti.ToLower('\u0130') != '\u0130' || ti.ToLower("ABCÉ") != "abcé" || ti.ToUpper("straße") != "STRAßE" || ti.ToString() != "TextInfo - ") return 51;
        if (ti.ToTitleCase("hello wORLD o'neil USA x1y ǆab") != "Hello World O'neil USA X1y ǅab" || !Throws<ArgumentNullException>(() => ti.ToTitleCase(null!))) return 52;
        if (!ti.IsReadOnly || ((TextInfo)ti.Clone()).IsReadOnly || !TextInfo.ReadOnly((TextInfo)ti.Clone()).IsReadOnly || !ti.Equals(new CultureInfo("").TextInfo)) return 53;

        // CompareInfo search, sort keys and identity.
        CompareInfo ci = inv.CompareInfo;
        if (ci.IndexOf("Hello", 'l') != 2 || ci.IndexOf("Hello", "LL", CompareOptions.IgnoreCase) != 2 || ci.IndexOf("Hello", "l", 3) != 3 || ci.IndexOf("Hello", 'o', 1, 3) != -1 || ci.IndexOf("abc", "") != 0) return 54;
        if (ci.LastIndexOf("Hello", 'l') != 3 || ci.LastIndexOf("Hello", "L", 2, CompareOptions.IgnoreCase) != 2 || ci.LastIndexOf("Hello", "l", 4, 2) != 3 || ci.LastIndexOf("", "", 0) != 0) return 55;
        if (!ci.IsPrefix("Hello", "he", CompareOptions.IgnoreCase) || ci.IsPrefix("Hello", "he") || !ci.IsSuffix("Hello", "LO", CompareOptions.OrdinalIgnoreCase) || !ci.IsSuffix("Hello", "") || !ci.IsPrefix("x", "", (CompareOptions)(-1))) return 56;
        if (!Throws<ArgumentException>(() => ci.IndexOf("a", "a", CompareOptions.Ordinal | CompareOptions.IgnoreCase)) || !Throws<ArgumentOutOfRangeException>(() => ci.IndexOf("abc", "b", 5)) || !Throws<ArgumentOutOfRangeException>(() => ci.LastIndexOf("abc", 'b', 5))) return 57;
        if (!Throws<ArgumentNullException>(() => ci.IndexOf(null!, 'a')) || !Throws<ArgumentException>(() => ci.IsPrefix("a", "a", CompareOptions.Ordinal | CompareOptions.IgnoreCase))) return 58;
        SortKey key = ci.GetSortKey("ab");
        if (!SameBytes(key.KeyData, 0, 97, 0, 98) || key.OriginalString != "ab" || !SameBytes(ci.GetSortKey("a", CompareOptions.IgnoreCase).KeyData, 0, 65) || ci.GetSortKey("").KeyData.Length != 0) return 59;
        if (SortKey.Compare(ci.GetSortKey("a"), ci.GetSortKey("b")) >= 0 || !key.Equals(ci.GetSortKey("ab")) || !Throws<ArgumentException>(() => ci.GetSortKey("x", CompareOptions.Ordinal))) return 60;
        if (ci.Version.FullVersion != 0 || ci.Version.SortId != new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x7F) || ci.LCID != 127 || ci.ToString() != "CompareInfo - ") return 61;
        if (!CompareInfo.IsSortable('a') || CompareInfo.IsSortable("") || CompareInfo.GetCompareInfo(127).Name != "" || !CompareInfo.GetCompareInfo("").Equals(ci) || !Throws<ArgumentException>(() => CompareInfo.GetCompareInfo(0x0c00))) return 62;

        // Text elements.
        var si = new StringInfo("áb\U0001F468‍\U0001F469");
        if (si.LengthInTextElements != 3 || si.SubstringByTextElements(1, 1) != "b" || si.SubstringByTextElements(2) != "\U0001F468‍\U0001F469" || si.String.Length != 8) return 63;
        if (StringInfo.GetNextTextElement("áb") != "á" || StringInfo.GetNextTextElement("áb", 2) != "b" || StringInfo.ParseCombiningCharacters("áb").Length != 2) return 64;
        TextElementEnumerator e = StringInfo.GetTextElementEnumerator("xá", 1);
        if (!e.MoveNext() || e.GetTextElement() != "á" || e.ElementIndex != 0 || (string)e.Current != "á" || e.MoveNext()) return 65;
        if (!Throws<ArgumentOutOfRangeException>(() => si.SubstringByTextElements(4)) || !new StringInfo("ab").Equals(new StringInfo("ab")) || new StringInfo().String != "") return 66;

        // RegionInfo in invariant mode.
        RegionInfo region = RegionInfo.CurrentRegion;
        if (region.Name != "IV" || region.EnglishName != "Invariant Country" || region.DisplayName != "Invariant Country" || region.NativeName != "Invariant Country" || region.ToString() != "IV") return 67;
        if (region.TwoLetterISORegionName != "IV" || region.ThreeLetterISORegionName != "ivc" || region.ThreeLetterWindowsRegionName != "ivc" || !region.IsMetric || region.GeoId != 244) return 68;
        if (region.ISOCurrencySymbol != "XDR" || region.CurrencySymbol != "¤" || region.CurrencyEnglishName != "International Monetary Fund" || region.CurrencyNativeName != "International Monetary Fund") return 69;
        if (!Throws<ArgumentException>(() => new RegionInfo("US")) || !Throws<ArgumentException>(() => new RegionInfo("")) || !Throws<ArgumentException>(() => new RegionInfo(127))) return 70;

        // Unicode digit values.
        if (CharUnicodeInfo.GetDecimalDigitValue('7') != 7 || CharUnicodeInfo.GetDecimalDigitValue('٣') != 3 || CharUnicodeInfo.GetDecimalDigitValue('²') != -1 || CharUnicodeInfo.GetDigitValue('²') != 2) return 71;
        if (CharUnicodeInfo.GetDigitValue("x\U0001D7D7", 1) != 9 || CharUnicodeInfo.GetDecimalDigitValue("a", 0) != -1 || !Throws<ArgumentNullException>(() => CharUnicodeInfo.GetDigitValue(null!, 0))) return 72;

        // Culture-taking String and Char overloads.
        if ("Hello".ToUpper(inv) != "HELLO" || "ABC".ToLower(null) != "abc" || !"Hello".StartsWith("he", true, null) || "Hello".StartsWith("he", false, inv) || !"Hello".EndsWith("LO", true, inv)) return 73;
        if ("aXbx".Replace("x", "_", true, null) != "a_b_" || "abc".Replace("z", "_", false, inv) != "abc" || !Throws<ArgumentException>(() => "a".Replace("", "b", true, null))) return 74;
        if (string.Compare("a", "B", true, inv) >= 0 || string.Compare("abc", 1, "xbc", 1, 2, false, null) != 0 || string.Compare("a", "A", inv, CompareOptions.IgnoreCase) != 0 || string.Compare("ab", 0, "AB", 0, 2, inv, CompareOptions.IgnoreCase) != 0) return 75;
        if (char.ToUpper('a', inv) != 'A' || char.ToLower('Q', inv) != 'q' || char.GetUnicodeCategory("a1", 1) != UnicodeCategory.DecimalDigitNumber || !Throws<ArgumentNullException>(() => char.ToUpper('a', null!))) return 76;

        // Calendar-based DateTime and DateTimeOffset construction.
        if (new DateTime(2024, 2, 29, new GregorianCalendar()) != new DateTime(2024, 2, 29) || new DateTime(2024, 1, 1, new JulianCalendar()) != new DateTime(2024, 1, 14)) return 77;
        var utc = new DateTime(2024, 1, 2, 3, 4, 5, 6, new GregorianCalendar(), DateTimeKind.Utc);
        if (utc.Kind != DateTimeKind.Utc || utc.Millisecond != 6 || new DateTime(5784, 1, 1, 0, 0, 0, new HebrewCalendar()) != new DateTime(2023, 9, 16)) return 78;
        var offset = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 6, new GregorianCalendar(), TimeSpan.FromHours(1));
        if (offset.Offset != TimeSpan.FromHours(1) || offset.Millisecond != 6 || !Throws<ArgumentNullException>(() => new DateTime(1, 1, 1, null!)) || !Throws<ArgumentOutOfRangeException>(() => new DateTime(1, 1, 1, 0, 0, 0, 1000, new GregorianCalendar(), DateTimeKind.Local))) return 79;

        // Passive values.
        if (new SortVersion(1, Guid.Empty) != new SortVersion(1, Guid.Empty) || new SortVersion(1, Guid.Empty) == new SortVersion(2, Guid.Empty) || new SortVersion(1, Guid.Empty).Equals(null)) return 80;
#pragma warning disable CS0618
        if ((int)CultureTypes.AllCultures != 7 || (int)CultureTypes.FrameworkCultures != 64 || (int)DigitShapes.NativeNational != 2) return 81;
#pragma warning restore CS0618
        return input + 1;
    }
}
