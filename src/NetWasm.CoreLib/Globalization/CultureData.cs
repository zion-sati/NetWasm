// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's stand-in for dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce's CultureData.
// NetWasm runs upstream's invariant globalization mode only, with PredefinedCulturesOnly in force,
// so the only culture record is upstream's CreateCultureWithInvariantData and every other name or
// LCID lookup fails as it does there. The members keep upstream's names and invariant-mode answers
// (including CalendarData.Invariant's Gregorian names and patterns) so the ported CultureInfo,
// DateTimeFormatInfo, TextInfo and RegionInfo code reads them unchanged. No ICU, NLS or host locale
// is consulted. Each name and pattern member builds its array when called rather than reading a
// static field, so reaching CultureData (to format a number, say) does not carry the calendar names.

namespace System.Globalization
{
    internal sealed class CultureData
    {
        private static CultureData? s_invariant;

        private CultureData()
        {
        }

        internal static CultureData Invariant => s_invariant ??= new CultureData();

        internal static CultureData? GetCultureData(string? cultureName, bool useUserOverride)
        {
            // The undetermined culture name "und" resolves to the invariant culture, as upstream's does.
            const string UndeterminedCultureName = "und";
            if (string.IsNullOrEmpty(cultureName) || cultureName.Equals(UndeterminedCultureName, StringComparison.OrdinalIgnoreCase))
            {
                return Invariant;
            }

            // Invariant mode with PredefinedCulturesOnly: no named culture exists.
            return null;
        }

        internal static CultureData GetCultureData(int culture, bool bUseUserOverride)
        {
            if (culture == CultureInfo.LOCALE_INVARIANT)
            {
                return Invariant;
            }

            // LCID is not supported in the InvariantMode
            throw new CultureNotFoundException(nameof(culture), culture, SR.Argument_CultureNotSupportedInInvariantMode);
        }

        internal static CultureData? GetCultureDataForRegion(string? cultureName, bool useUserOverride)
        {
            if (string.IsNullOrEmpty(cultureName))
            {
                return Invariant;
            }

            // Upstream's region-name table maps names to cultures that invariant mode cannot create.
            CultureData? retVal = GetCultureData(cultureName, useUserOverride);
            return retVal is { IsNeutralCulture: false } ? retVal : null;
        }

        internal static CultureData GetCurrentRegionData() => CultureInfo.CurrentCulture._cultureData;

        internal static CultureInfo[] GetCultures(CultureTypes types)
        {
            // Disable  warning 618: System.Globalization.CultureTypes.FrameworkCultures' is obsolete
#pragma warning disable 618
            // Validate flags
            if ((int)types <= 0 || ((int)types & (int)~(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures |
                                                        CultureTypes.InstalledWin32Cultures | CultureTypes.UserCustomCulture |
                                                        CultureTypes.ReplacementCultures | CultureTypes.WindowsOnlyCultures |
                                                        CultureTypes.FrameworkCultures)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(types),
                              SR.Format(SR.ArgumentOutOfRange_Range, CultureTypes.NeutralCultures, CultureTypes.FrameworkCultures));
            }
#pragma warning restore 618

            // in invariant mode we always return invariant culture only from the enumeration
            return [new CultureInfo("")];
        }

        internal static void ClearCachedData()
        {
        }

        internal static bool IsWin32Installed => false;

        internal static string AnsiToLower(string testString) => TextInfo.ToLowerAsciiInvariant(testString);

        // Identity
        internal string CultureName => string.Empty;
        internal bool UseUserOverride => false;
        internal string Name => string.Empty;
        internal string SortName => string.Empty;
        internal string? InteropName => string.Empty;
        internal string ParentName => string.Empty;
        internal string DisplayName => NativeName;
        internal string EnglishName => "Invariant Language (Invariant Country)";
        internal string NativeName => "Invariant Language (Invariant Country)";
        internal string SpecificCultureName => string.Empty;
        internal string TwoLetterISOLanguageName => "iv";
        internal string ThreeLetterISOLanguageName => "ivl";
        internal string ThreeLetterWindowsLanguageName => "IVL";
        internal string SCONSOLEFALLBACKNAME => string.Empty;
        internal int LCID => CultureInfo.LOCALE_INVARIANT;
        internal int KeyboardLayoutId => CultureInfo.LOCALE_INVARIANT;
        internal bool IsNeutralCulture => false;
        internal bool IsInvariantCulture => true;
        internal bool IsReplacementCulture => false;
        internal bool IsSupplementalCustomCulture => false;
        internal Calendar DefaultCalendar => new GregorianCalendar();
        internal CalendarId[] CalendarIds => [CalendarId.GREGORIAN];

        // Text information
        internal int ANSICodePage => 1252;
        internal int OEMCodePage => 437;
        internal int MacCodePage => 10000;
        internal int EBCDICCodePage => 037;
        internal bool IsRightToLeft => false;
        internal string TextInfoName => string.Empty;
        internal string ListSeparator => ",";

        // Region
        internal string RegionName => "IV";
        internal string EnglishCountryName => "Invariant Country";
        internal string NativeCountryName => "Invariant Country";
        internal string LocalizedCountryName => NativeCountryName;
        internal string TwoLetterISOCountryName => "IV";
        internal string ThreeLetterISOCountryName => "ivc";
        internal int GeoId => 244;
        internal int MeasurementSystem => 0;
        internal string CurrencySymbol => "\x00a4";
        internal string ISOCurrencySymbol => "XDR";
        internal string CurrencyEnglishName => "International Monetary Fund";
        internal string CurrencyNativeName => "International Monetary Fund";

        // Time and calendar
        internal string AMDesignator => "AM";
        internal string PMDesignator => "PM";
        internal string TimeSeparator => ":";
        internal int FirstDayOfWeek => 0;
        internal int CalendarWeekRule => 0;
        internal string[] LongTimes => ["HH:mm:ss"];
        internal string[] ShortTimes => ["HH:mm", "hh:mm tt", "H:mm", "h:mm tt"];

        // Every calendar answers from CalendarData.Invariant in invariant mode.
        internal string DateSeparator(CalendarId calendarId) => "/";
        internal string[] ShortDates(CalendarId calendarId) => ["MM/dd/yyyy", "yyyy-MM-dd"];
        internal string[] LongDates(CalendarId calendarId) => ["dddd, dd MMMM yyyy"];
        internal string[] YearMonths(CalendarId calendarId) => ["yyyy MMMM"];
        internal string MonthDay(CalendarId calendarId) => "MMMM dd";
        internal string[] DayNames(CalendarId calendarId) => ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
        internal string[] AbbreviatedDayNames(CalendarId calendarId) => ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        internal string[] SuperShortDayNames(CalendarId calendarId) => ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
        internal string[] MonthNames(CalendarId calendarId) => ["January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December", string.Empty];
        internal string[] AbbreviatedMonthNames(CalendarId calendarId) => ["Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", string.Empty];
        internal string[] GenitiveMonthNames(CalendarId calendarId) => ["January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December", string.Empty];
        internal string[] AbbreviatedGenitiveMonthNames(CalendarId calendarId) => ["Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", string.Empty];
        internal string[] LeapYearMonthNames(CalendarId calendarId) => ["January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December", string.Empty];
        internal string[] EraNames(CalendarId calendarId) => ["A.D."];
        internal string[] AbbrevEraNames(CalendarId calendarId) => ["AD"];
        internal string[] AbbreviatedEnglishEraNames(CalendarId calendarId) => ["AD"];
        internal string CalendarName(CalendarId calendarId) => "Gregorian Calendar";
    }
}
