using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// DateTime, DateTimeOffset, DateOnly, TimeOnly and TimeSpan text against desktop, with the invariant culture:
// general parsing (RFC 1123, month names, partial dates, offsets, AM/PM, eras, CJK suffixes and permissive
// punctuation), every standard format, custom formats, exact parsing, styles, failures and span formatting. Each
// input returns a hash of what its scenario observed. Local results are observed as UTC instants, nothing reads the
// current culture, and time-only text defaults to no date, so the program and desktop agree in any time zone and
// locale.
public sealed class DateTimeTextCorpusTests(CorrectnessTestRunner runner)
{
    private const string Namespace = "NetWasm.Correctness.DateTimeText";

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Text;

        namespace NetWasm.Correctness.DateTimeText;

        public static class EntryPoint
        {
            private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

            public static int Trace() => 0;

            public static int Run(int input)
            {
                var seen = new List<string>();
                try
                {
                    Scenario(input, seen);
                }
                catch (Exception exception)
                {
                    seen.Add("threw " + Kind(exception));
                }
                var hash = 17;
                foreach (var line in seen)
                {
                    foreach (var c in line)
                    {
                        hash = unchecked(hash * 31 + c);
                    }
                    hash = unchecked(hash * 31 + '|');
                }
                return hash & 0x7fffffff;
            }

            // Classified by type test: a program without type names reads type#<id> for a name.
            private static string Kind(Exception exception) =>
                exception is FormatException ? "format" : exception is ArgumentOutOfRangeException ? "range" :
                exception is ArgumentNullException ? "null" : exception is ArgumentException ? "argument" :
                exception is OverflowException ? "overflow" : "other";

            private static readonly string[] GeneralDateTimes =
            {
                "2024-01-15", "01/15/2024", "1/15/2024", "01/15/2024 10:30:00", "1/15/2024 10:30 PM", "Jan 15, 2024",
                "15 January 2024", "Mon, 15 Jan 2024 10:00:00 GMT", "Monday, 15 January 2024", "2024-01-15T10:00:00",
                "2024-01-15T10:00:00Z", "2024-01-15T10:00:00.1234567+02:00", "2024-01-15 10:30:00Z", "2024/01/15",
                "15-Jan-2024", "20240115", "Wed, 09 Jun 2100 10:18:14 GMT", "2024-13-01", " 2024-01-15 ",
                "February 2024", "2024/02", "02/2024", "29 2024 Feb", "February 2024 29", "Feb29 2024", "29Feb2024",
                "2024-02-29 AD", "AD 2024-02-29", "2024年2月29日", "February 99 1", "99 February 1",
                ",2024-2-29", "2024-2-29,", "2024-02-30", "Tue, 15 Jan 2024 10:00:00 GMT", "2024-01-15T24:00:00",
                "2024-01-15T10:00:00-14:00", "2024-01-15T10:00:00+15:00",
                "9999-12-31T23:59:59.9999999", "12/31/9999 11:59:59 PM", "2024-01-15T10:00:00.12345678",
                "1/2/3", "10/11/12 13:14", "2024-01-15 T 10:00", "",
            };

            private static readonly string[] TimesOfDay =
            {
                "10:30", "10:30:15 AM", "13:04.", "1 AM,", "1:02:03.4,PM", "23:59:59.9999999", "24:00", "12 PM", "12 AM",
                "1:2:3", "10:30 GMT", "10:30+02:00",
            };

            // A local result prints as its UTC instant, so the program and desktop agree in any time zone.
            private static string Show(DateTime value) =>
                (value.Kind == DateTimeKind.Local ? value.ToUniversalTime().ToString("o", Inv) : value.ToString("o", Inv)) + " " + value.Kind;

            private static void Scenario(int input, List<string> seen)
            {
                var utc = new DateTime(2024, 1, 15, 22, 5, 9, 123, DateTimeKind.Utc).AddTicks(4567);
                var unspecified = new DateTime(2009, 6, 1, 7, 8, 9, 10, DateTimeKind.Unspecified);
                switch (input)
                {
                    case 0:
                        // General parsing, keeping the kind the text gives.
                        foreach (var text in GeneralDateTimes)
                        {
                            seen.Add(DateTime.TryParse(text, Inv, DateTimeStyles.RoundtripKind, out var value) ? Show(value) : "fail " + text);
                        }
                        break;
                    case 1:
                        // General parsing with the universal styles and white space.
                        foreach (var text in GeneralDateTimes)
                        {
                            seen.Add(DateTime.TryParse(text, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal |
                                DateTimeStyles.AllowWhiteSpaces, out var value) ? Show(value) : "fail");
                        }
                        // Converting to local would clamp at the minimum by an amount the time zone decides.
                        seen.Add(DateTime.TryParse("0001-01-01T00:00:00+01:00", Inv, DateTimeStyles.AdjustToUniversal, out var edge) ? Show(edge) : "fail");
                        foreach (var text in TimesOfDay)
                        {
                            seen.Add(DateTime.TryParse(text, Inv, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal |
                                DateTimeStyles.AssumeUniversal, out var value) ? Show(value) : "fail");
                        }
                        break;
                    case 2:
                        // Every standard format, for a UTC and an unspecified time. U converts an unspecified time
                        // from local time, so it reads the time zone and is observed on the UTC time only.
                        foreach (var format in "dDfFgGmMoOrRstTuUyY")
                        {
                            seen.Add(format + " " + utc.ToString(format.ToString(), Inv) + " | " +
                                (format == 'U' ? "" : unspecified.ToString(format.ToString(), Inv)));
                        }
                        seen.Add(utc.ToString(Inv) + " | " + unspecified.ToString(Inv));
                        foreach (var format in "dDfFgGmMoOrRstTuyY")
                        {
                            seen.Add(string.Join(";", unspecified.GetDateTimeFormats(format, Inv)));
                        }
                        seen.Add(unspecified.GetDateTimeFormats(Inv).Length.ToString());
                        break;
                    case 3:
                        // Custom formats.
                        foreach (var format in new[] { "dddd, MMMM d yyyy", "ddd MMM", "hh:mm tt", "yy-M-d H:m:s.fff", "%K", "gg yyyy",
                            "yyyyy", "%y", "FFFFFFF", "fffffff", "F", "%h", "HH 'o''clock'", "\\d\\a\\y d", "MMMMM", "t", "%t",
                            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "'unterminated", "%", "\\", "d MMMM", "MMMM d", "dd/MM", "z", "zz", "zzz" })
                        {
                            try
                            {
                                // z reads the local offset of an unspecified time, so it is observed on the UTC time only.
                                seen.Add(format + " => " + utc.ToString(format, Inv) + " | " +
                                    (format.Contains('z') ? "" : unspecified.ToString(format, Inv)));
                            }
                            catch (Exception exception)
                            {
                                seen.Add(format + " threw " + Kind(exception));
                            }
                        }
                        break;
                    case 4:
                        // DateTimeOffset text, with explicit offsets.
                        var offsets = new[] { new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.FromHours(2)),
                            new DateTimeOffset(2024, 7, 1, 23, 59, 59, 999, new TimeSpan(-5, -30, 0)), new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero) };
                        foreach (var value in offsets)
                        {
                            foreach (var format in new[] { "o", "O", "r", "s", "u", "zzz", "K", "yyyy-MM-dd HH:mm:ss zzz", "G", "D", "f", "U" })
                            {
                                try { seen.Add(value.ToString(format, Inv)); } catch (Exception exception) { seen.Add(format + " threw " + Kind(exception)); }
                            }
                            seen.Add(value.ToString(Inv));
                        }
                        foreach (var text in new[] { "2024-01-15T10:00:00+02:00", "2024-01-15T10:00:00Z", "Mon, 15 Jan 2024 10:00:00 GMT",
                            "1/15/2024 10:00 AM -05:00", "2024-01-15 10:00:00 +14:00", "2024-01-15 10:00:00 +14:01", "2024-01-15T10:00:00",
                            "0001-01-01T00:00:00+01:00" })
                        {
                            seen.Add(DateTimeOffset.TryParse(text, Inv, DateTimeStyles.AssumeUniversal, out var parsed)
                                ? parsed.ToString("o", Inv) : "fail");
                        }
                        seen.Add(DateTimeOffset.ParseExact("15/01/2024 10:00 +02:00", "dd/MM/yyyy HH:mm zzz", Inv).ToString("o", Inv));
                        try { DateTimeOffset.Parse("2024-01-15", Inv, DateTimeStyles.RoundtripKind | DateTimeStyles.NoCurrentDateDefault); }
                        catch (Exception exception) { seen.Add("styles " + Kind(exception)); }
                        break;
                    case 5:
                        // DateOnly text, including desktop's general grammar.
                        foreach (var text in GeneralDateTimes)
                        {
                            seen.Add(DateOnly.TryParse(text, Inv, DateTimeStyles.None, out var date) ? date.ToString("o", Inv) : "fail");
                        }
                        var day = new DateOnly(2024, 2, 29);
                        foreach (var format in new[] { "d", "D", "m", "M", "o", "O", "r", "R", "y", "Y", "dddd yyyy-MM-dd", "yy MMM" })
                        {
                            seen.Add(day.ToString(format, Inv));
                        }
                        foreach (var format in new[] { "t", "T", "HH", "f", "G" })
                        {
                            try { seen.Add(day.ToString(format, Inv)); } catch (Exception exception) { seen.Add(format + " threw " + Kind(exception)); }
                        }
                        seen.Add(day.ToString(Inv));
                        seen.Add(DateOnly.ParseExact("29.02.2024", "dd.MM.yyyy", Inv).ToString("o", Inv));
                        seen.Add(DateOnly.TryParseExact("2024-02-29 10:00", "yyyy-MM-dd HH:mm", Inv, DateTimeStyles.None, out _).ToString());
                        break;
                    case 6:
                        // TimeOnly text.
                        foreach (var text in TimesOfDay)
                        {
                            seen.Add(TimeOnly.TryParse(text, Inv, DateTimeStyles.None, out var time) ? time.ToString("o", Inv) : "fail");
                        }
                        var clock = new TimeOnly(13, 4, 5, 678).Add(TimeSpan.FromTicks(9));
                        foreach (var format in new[] { "t", "T", "o", "O", "r", "R", "hh:mm:ss tt", "HH:mm:ss.fffffff", "%h" })
                        {
                            seen.Add(clock.ToString(format, Inv));
                        }
                        foreach (var format in new[] { "d", "yyyy", "D", "MMM" })
                        {
                            try { seen.Add(clock.ToString(format, Inv)); } catch (Exception exception) { seen.Add(format + " threw " + Kind(exception)); }
                        }
                        seen.Add(clock.ToString(Inv));
                        seen.Add(TimeOnly.ParseExact("01:02 PM", "hh:mm tt", Inv).ToString("o", Inv));
                        break;
                    case 7:
                        // TimeSpan text.
                        var spans = new[] { TimeSpan.Zero, new TimeSpan(1, 2, 3, 4, 567), TimeSpan.FromTicks(-123456789012),
                            TimeSpan.MaxValue, TimeSpan.MinValue, TimeSpan.FromMilliseconds(1.5) };
                        foreach (var span in spans)
                        {
                            foreach (var format in new[] { "c", "g", "G", @"hh\:mm", @"d\.hh\:mm\:ss\.fffffff", @"%d", @"FFFFFFF", @"mm\:ss\.ff" })
                            {
                                try { seen.Add(span.ToString(format, Inv)); } catch (Exception exception) { seen.Add(format + " threw " + Kind(exception)); }
                            }
                            seen.Add(span.ToString());
                        }
                        foreach (var text in new[] { "1.02:03:04.5670000", "02:03", "-02:03:04", "1:2:3:4.5", "10675199.02:48:05.4775807",
                            "10675200.00:00:00", "00:60:00", "1.24:00:00", " 3 ", "1:2:3.12345678", "-0:0:0.1", "1.2:3" })
                        {
                            try { seen.Add(TimeSpan.Parse(text, Inv).ToString("c")); } catch (Exception exception) { seen.Add("threw " + Kind(exception)); }
                        }
                        seen.Add(TimeSpan.ParseExact("5:06", @"m\:ss", Inv, TimeSpanStyles.AssumeNegative).ToString());
                        seen.Add(TimeSpan.TryParseExact("1:2", new[] { "g", @"h\:m" }, Inv, out var exact) + " " + exact);
                        break;
                    case 8:
                        // Exact parsing, with format arrays and styles.
                        foreach (var (text, format) in new[] { ("2024-01-15 10:30", "yyyy-MM-dd HH:mm"), ("15/01/24", "dd/MM/yy"),
                            ("Monday, January 15, 2024", "dddd, MMMM d, yyyy"), ("Tuesday, January 15, 2024", "dddd, MMMM d, yyyy"),
                            ("2024-01-15T10:30:00.1234567Z", "o"), ("Mon, 15 Jan 2024 10:30:00 GMT", "r"), ("2024-01-15T10:30:00", "s"),
                            ("2024-01-15 10:30:00Z", "u"), ("  2024-01-15", "yyyy-MM-dd"), ("10:30 PM", "h:mm tt"), ("2024", "yyyy"),
                            ("01/15/2024", "d"), ("1/15/2024 10:30:00 PM", "G"), ("20240115", "yyyyMMdd"), ("2024-01-15 +02:00", "yyyy-MM-dd zzz") })
                        {
                            seen.Add(DateTime.TryParseExact(text, format, Inv, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal |
                                DateTimeStyles.NoCurrentDateDefault, out var value) ? Show(value) : "fail");
                        }
                        seen.Add(DateTime.TryParseExact("15.01.2024", new[] { "yyyy-MM-dd", "dd.MM.yyyy" }, Inv, DateTimeStyles.None, out var multiple)
                            ? Show(multiple) : "fail");
                        seen.Add(Show(DateTime.ParseExact("2024-01-15T10:30:00Z", "yyyy-MM-ddTHH:mm:ssK", Inv, DateTimeStyles.RoundtripKind)));
                        break;
                    case 9:
                        // Failures and their exception types.
                        foreach (var action in new Action[]
                        {
                            () => DateTime.Parse("not a date", Inv),
                            () => DateTime.Parse(null!, Inv),
                            () => DateTime.ParseExact("2024", "", Inv),
                            () => DateTime.ParseExact("2024", "yyyy", Inv, (DateTimeStyles)0x1000),
                            () => DateTime.Parse("2024-01-15", Inv, DateTimeStyles.AssumeLocal | DateTimeStyles.AssumeUniversal),
                            () => DateTime.Parse("2024-01-15", Inv, DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal),
                            () => utc.ToString("Q", Inv),
                            () => utc.ToString("'", Inv),
                            () => TimeSpan.Parse("1:60", Inv),
                            () => TimeSpan.Parse("99999999.00:00:00", Inv),
                            () => TimeSpan.ParseExact("1", "c", Inv, (TimeSpanStyles)2),
                            () => DateOnly.Parse("2024-01-15 10:00", Inv),
                            () => TimeOnly.Parse("2024-01-15", Inv),
                            () => DateTimeOffset.Parse("2024-01-15T10:00:00+15:00", Inv),
                        })
                        {
                            try { action(); seen.Add("ok"); } catch (Exception exception) { seen.Add(Kind(exception)); }
                        }
                        break;
                    case 10:
                        // Span formatting, into exact, short and UTF-8 buffers.
                        Span<char> chars = stackalloc char[64];
                        Span<byte> bytes = stackalloc byte[64];
                        foreach (var format in new[] { "", "o", "r", "s", "u", "D", "dddd yyyy" })
                        {
                            var ok = utc.TryFormat(chars, out var written, format, Inv);
                            seen.Add(ok + " " + chars.Slice(0, written).ToString());
                            seen.Add(utc.TryFormat(chars.Slice(0, 5), out written, format, Inv) + " " + written);
                            var okUtf8 = utc.TryFormat(bytes, out var bytesWritten, format, Inv);
                            seen.Add(okUtf8 + " " + Encoding.UTF8.GetString(bytes.Slice(0, bytesWritten)));
                        }
                        seen.Add(new TimeSpan(1, 2, 3).TryFormat(chars, out var spanWritten, "g", Inv) + " " + chars.Slice(0, spanWritten).ToString());
                        seen.Add(new DateOnly(2024, 2, 29).TryFormat(chars, out var dateWritten, "D", Inv) + " " + chars.Slice(0, dateWritten).ToString());
                        seen.Add(new TimeOnly(1, 2).TryFormat(chars, out var timeWritten, "T", Inv) + " " + chars.Slice(0, timeWritten).ToString());
                        seen.Add(new DateTimeOffset(utc).TryFormat(chars, out var offsetWritten, "o", Inv) + " " + chars.Slice(0, offsetWritten).ToString());
                        break;
                    case 11:
                        // A changed DateTimeFormatInfo reaches formatting and parsing.
                        var info = (DateTimeFormatInfo)Inv.DateTimeFormat.Clone();
                        info.DateSeparator = "-";
                        info.TimeSeparator = ".";
                        info.ShortDatePattern = "yyyy/MM/dd";
                        info.AMDesignator = "am";
                        info.PMDesignator = "pm";
                        info.MonthNames = new[] { "Ja", "Fe", "Ma", "Ap", "My", "Jn", "Jl", "Au", "Se", "Oc", "No", "De", "" };
                        seen.Add(utc.ToString("d", info) + " " + utc.ToString("t", info) + " " + utc.ToString("hh:mm tt MMMM", info));
                        seen.Add(DateTime.TryParse("2024-01-15 10.30 pm", info, DateTimeStyles.None, out var custom) ? Show(custom) : "fail");
                        seen.Add(DateTime.TryParse("15 Fe 2024", info, DateTimeStyles.None, out var named) ? Show(named) : "fail");
                        break;
                }
            }
        }
        """;

    // Partial dates read today's date and offsets convert to local time, which read the clock and the time zone, so
    // the program needs the platform world.
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void DateTimeTextMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new CorpusFixture("DateTimeTextCorpus" + optimization, Namespace, Source, [.. Enumerable.Range(0, 12)])
        {
            RequiresReactor = true,
            ReportAllMismatches = true,
            ExecuteOptimizedWasm = true,
            Optimization = optimization,
        });
}
