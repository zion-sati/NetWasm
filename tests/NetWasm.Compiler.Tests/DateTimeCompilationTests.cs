using System.Collections.Immutable;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class DateTimeCompilationTests
{
    [Fact]
    public void CompilerParsesInvariantDateOnlyAndTimeOnlyContracts()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System;

            namespace DateOnlyTimeOnlyParsingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (!global::System.DateOnly.TryParse("02/29/2024", null, out var shortDate) ||
                        shortDate != new global::System.DateOnly(2024, 2, 29) ||
                        !global::System.DateOnly.TryParse("29 February 2024", null, out var namedDate) ||
                        namedDate != shortDate ||
                        !global::System.DateOnly.TryParse("Thu, 29 Feb 2024", null, out var rfcDate) ||
                        rfcDate != shortDate)
                    {
                        return 1;
                    }

                    if (!global::System.DateOnly.TryParse("2/9/2024", null, out var flexibleDate) ||
                        flexibleDate != new global::System.DateOnly(2024, 2, 9) ||
                        !global::System.DateOnly.TryParse(" 02/29/2024 ", null, out flexibleDate) ||
                        flexibleDate != shortDate ||
                        !global::System.DateOnly.TryParse("2024/02/29", null, out flexibleDate) ||
                        flexibleDate != shortDate ||
                        !global::System.DateOnly.TryParse("February 29 2024", null, out flexibleDate) ||
                        flexibleDate != shortDate ||
                        !global::System.DateOnly.TryParse("02/29/24", null, out flexibleDate) ||
                        flexibleDate != shortDate)
                    {
                        return 11;
                    }

                    if (!global::System.DateOnly.TryParse(
                            "02/29/2024 Thursday", null, out flexibleDate) ||
                        flexibleDate != shortDate)
                    {
                        return 16;
                    }

                    if (!global::System.DateOnly.TryParse(
                            "Feb 29, 2024", null, out flexibleDate) ||
                        flexibleDate != shortDate ||
                        !global::System.DateOnly.TryParse(
                            "February 29, 2024", null, out flexibleDate) ||
                        flexibleDate != shortDate ||
                        !global::System.DateOnly.TryParse(
                            "Feb, 29, 2024", null, out flexibleDate) ||
                        flexibleDate != shortDate ||
                        !global::System.DateOnly.TryParse(
                            "2,29,2024", null, out flexibleDate) ||
                        flexibleDate != shortDate)
                    {
                        return 17;
                    }

                    if (!global::System.DateOnly.TryParse(
                            "02/29/2024\0", null, out flexibleDate) ||
                        flexibleDate != shortDate)
                    {
                        return 18;
                    }

                    if (
                        global::System.DateOnly.TryParse("2024", null, out _) ||
                        global::System.DateOnly.TryParse("February", null, out _) ||
                        global::System.DateOnly.TryParse("February 2024", null, out _) ||
                        global::System.DateOnly.TryParse("2024/02", null, out _) ||
                        global::System.DateOnly.TryParse("02/2024", null, out _) ||
                        global::System.DateOnly.TryParse("29 2024 Feb", null, out _) ||
                        global::System.DateOnly.TryParse("February 2024 29", null, out _) ||
                        global::System.DateOnly.TryParse("Feb 2024 29", null, out _) ||
                        global::System.DateOnly.TryParse("Feb29 2024", null, out _) ||
                        global::System.DateOnly.TryParse("29Feb2024", null, out _) ||
                        global::System.DateOnly.TryParse("2024-02-29 AD", null, out _) ||
                        global::System.DateOnly.TryParse("AD 2024-02-29", null, out _) ||
                        global::System.DateOnly.TryParse("2024年2月29日", null, out _) ||
                        global::System.DateOnly.TryParse("February 99 1", null, out _) ||
                        global::System.DateOnly.TryParse("99 February 1", null, out _) ||
                        global::System.DateOnly.TryParse(",2024-2-29", null, out _) ||
                        global::System.DateOnly.TryParse("2024-2-29,", null, out _) ||
                        global::System.DateOnly.TryParse("2024-02-29/", null, out _) ||
                        global::System.DateOnly.TryParse("2024--02--29", null, out _) ||
                        global::System.DateOnly.TryParse("02/29/2024\0x", null, out _))
                    {
                        return 19;
                    }

                    if (!global::System.TimeOnly.TryParse("1:04 PM", null, out var meridiem) ||
                        meridiem != new global::System.TimeOnly(13, 4) ||
                        !global::System.TimeOnly.TryParse("24:00:00", null, out var midnight) ||
                        midnight != global::System.TimeOnly.MinValue ||
                        global::System.TimeOnly.TryParse("24:00:01", null, out _) ||
                        global::System.TimeOnly.TryParse("24:00:00.0000001", null, out _))
                    {
                        return 2;
                    }

                    if (!global::System.TimeOnly.TryParse("1 PM", null, out var flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 0) ||
                        !global::System.TimeOnly.TryParse("13:4", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 4) ||
                        !global::System.TimeOnly.TryParse(" 13:04 ", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 4) ||
                        !global::System.TimeOnly.TryParse(
                            "13:04:05.12345678", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 4, 5).Add(
                            global::System.TimeSpan.FromTicks(1234568)) ||
                        !global::System.TimeOnly.TryParse(
                            "13:04:59.99999995", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 5) ||
                        !global::System.TimeOnly.TryParse(
                            "13:59:59.99999995", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(14, 0) ||
                        !global::System.TimeOnly.TryParse(
                            "23:59:59.99999995", null, out flexibleTime) ||
                        flexibleTime != global::System.TimeOnly.MinValue ||
                        !global::System.TimeOnly.TryParse(
                            "13:04:05.", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 4, 5) ||
                        !global::System.TimeOnly.TryParse(
                            "13:04:05.12345685", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 4, 5).Add(
                            global::System.TimeSpan.FromTicks(1234568)) ||
                        global::System.TimeOnly.TryParse("13", null, out _) ||
                        global::System.TimeOnly.TryParse("13 AM", null, out _) ||
                        global::System.TimeOnly.TryParse("13:04 AM", null, out _) ||
                        global::System.TimeOnly.TryParse("24:00 AM", null, out _) ||
                        global::System.TimeOnly.TryParse("24:00 PM", null, out _) ||
                        global::System.TimeOnly.TryParse("1 P", null, out _) ||
                        global::System.TimeOnly.TryParse("13:04.", null, out _) ||
                        global::System.TimeOnly.TryParse("1 AM,", null, out _) ||
                        global::System.TimeOnly.TryParse("1:02:03.4,PM", null, out _) ||
                        !global::System.TimeOnly.TryParse(
                            "13:04\0", null, out flexibleTime) ||
                        flexibleTime != new global::System.TimeOnly(13, 4) ||
                        global::System.TimeOnly.TryParse("13:04\0x", null, out _) ||
                        global::System.TimeOnly.TryParse(
                            "24:00:00.00000004", null, out _) ||
                        global::System.TimeOnly.TryParse(
                            "24:00:00.00000005", null, out _))
                    {
                        return 12;
                    }

                    if (global::System.DateOnly.TryParseExact("2024-02-29", "d", out _) ||
                        !global::System.DateOnly.TryParseExact("2024-02-29", "O", out var exactDate) ||
                        exactDate != shortDate ||
                        !global::System.DateOnly.TryParseExact("20240229", "yyyyMMdd", out exactDate) ||
                        exactDate != shortDate ||
                        !global::System.TimeOnly.TryParseExact("13:04", "HH:mm", out var exactTime) ||
                        exactTime != new global::System.TimeOnly(13, 4) ||
                        global::System.TimeOnly.TryParseExact("13:04", "T", out _) ||
                        !global::System.TimeOnly.TryParseExact(
                            "13:04:05.1200000", "O", out exactTime) ||
                        exactTime != new global::System.TimeOnly(13, 4, 5, 120))
                    {
                        return 3;
                    }

                    var leading = global::System.Globalization.DateTimeStyles.AllowLeadingWhite;
                    var trailing = global::System.Globalization.DateTimeStyles.AllowTrailingWhite;
                    var inner = global::System.Globalization.DateTimeStyles.AllowInnerWhite;
                    if (!global::System.DateOnly.TryParseExact(
                            "\u00a02024-02-29".AsSpan(),
                            "O".AsSpan(),
                            null,
                            leading,
                            out _) ||
                        global::System.DateOnly.TryParseExact(
                            " 2024-02-29".AsSpan(),
                            "O".AsSpan(),
                            null,
                            trailing,
                            out _) ||
                        !global::System.DateOnly.TryParseExact(
                            "2024 - 02 - 29".AsSpan(),
                            "yyyy - MM - dd".AsSpan(),
                            null,
                            inner,
                            out _))
                    {
                        return 4;
                    }

                    var provider = new CountingProvider();
                    if (!global::System.DateOnly.TryParse("02/29/2024", provider, out _) ||
                        provider.Calls != 1 ||
                        !global::System.DateOnly.TryParseExact("2024-02-29", "O", provider,
                            global::System.Globalization.DateTimeStyles.None, out _) ||
                        provider.Calls != 1 ||
                        !global::System.DateOnly.TryParseExact(
                            "2024-02-29".AsSpan(),
                            new[] { "O" },
                            provider,
                            global::System.Globalization.DateTimeStyles.None,
                            out _) ||
                        provider.Calls != 2)
                    {
                        return 5;
                    }

                    try
                    {
                        _ = global::System.DateOnly.TryParseExact(
                            "2024-02-29".AsSpan(),
                            new string[] { "O", null! },
                            null,
                            global::System.Globalization.DateTimeStyles.None,
                            out _);
                        return 6;
                    }
                    catch (global::System.FormatException)
                    {
                    }

                    if (global::System.DateOnly.TryParseExact(
                            "2024-02-29".AsSpan(),
                            new[] { "q", "O" },
                            null,
                            global::System.Globalization.DateTimeStyles.None,
                            out _))
                    {
                        return 7;
                    }

                    if (!global::System.DateOnly.TryParseExact(
                            "2024-02-29 13:04:05",
                            "yyyy-MM-dd HH:mm:ss",
                            out var dateWithTime) ||
                        dateWithTime != shortDate ||
                        global::System.DateOnly.TryParseExact(
                            "2024-02-29 24:00:01",
                            "yyyy-MM-dd HH:mm:ss",
                            out _) ||
                        !global::System.TimeOnly.TryParseExact(
                            "2024-02-29 13:04",
                            "yyyy-MM-dd HH:mm",
                            out var timeWithDate) ||
                        timeWithDate != new global::System.TimeOnly(13, 4) ||
                        global::System.TimeOnly.TryParseExact(
                            "2023-02-29 13:04",
                            "yyyy-MM-dd HH:mm",
                            out _) ||
                        !global::System.DateOnly.TryParseExact(
                            "2024 2024-02-29",
                            "yyyy yyyy-MM-dd",
                            out _) ||
                        global::System.DateOnly.TryParseExact(
                            "2024 2025-02-29",
                            "yyyy yyyy-MM-dd",
                            out _))
                    {
                        return 8;
                    }

                    if (!global::System.DateOnly.TryParseExact(
                            "Thursday, 29 February 002024",
                            "ddddd, dd MMMMM yyyyyy",
                            out var repeatedDate) ||
                        repeatedDate != shortDate ||
                        !global::System.TimeOnly.TryParseExact(
                            "13:04", "HHH:mm", out var repeatedTime) ||
                        repeatedTime != new global::System.TimeOnly(13, 4) ||
                        !global::System.TimeOnly.TryParseExact(
                            "13:04 PM", "HH:mm tt", out repeatedTime) ||
                        repeatedTime != new global::System.TimeOnly(13, 4) ||
                        !global::System.TimeOnly.TryParseExact(
                            "00:04 AM", "hh:mm tt", out repeatedTime) ||
                        repeatedTime != new global::System.TimeOnly(0, 4))
                    {
                        return 13;
                    }

                    var allWhite = global::System.Globalization.DateTimeStyles.AllowWhiteSpaces;
                    if (!global::System.DateOnly.TryParseExact(
                            "2024-02-29".AsSpan(),
                            "yyyy - MM - dd".AsSpan(),
                            null,
                            inner,
                            out _) ||
                        !global::System.DateOnly.TryParseExact(
                            "2024 -02-29".AsSpan(),
                            "yyyy-MM-dd".AsSpan(),
                            null,
                            inner,
                            out _) ||
                        !global::System.DateOnly.TryParseExact(
                            "2024-02-29".AsSpan(),
                            " yyyy-MM-dd ".AsSpan(),
                            null,
                            allWhite,
                            out _) ||
                        global::System.DateOnly.TryParseExact("", "''", out _) ||
                        global::System.TimeOnly.TryParseExact("", "''", out _))
                    {
                        return 14;
                    }

                    try
                    {
                        _ = global::System.DateOnly.TryParse(
                            "02/29/2024",
                            new ThrowingProvider(),
                            out _);
                        return 9;
                    }
                    catch (global::System.InvalidOperationException)
                    {
                    }

                    if (!global::System.DateOnly.TryParseExact(
                            "2024-02-29",
                            "O",
                            new ThrowingProvider(),
                            global::System.Globalization.DateTimeStyles.None,
                            out _))
                    {
                        return 10;
                    }

                    return input;
                }

                private sealed class CountingProvider : global::System.IFormatProvider
                {
                    public int Calls;

                    public object? GetFormat(global::System.Type? formatType)
                    {
                        Calls++;
                        return null;
                    }
                }

                private sealed class ThrowingProvider : global::System.IFormatProvider
                {
                    public object? GetFormat(global::System.Type? formatType) =>
                        throw new global::System.InvalidOperationException();
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                var result = executor.Execute(new CompilationScenario(
                    $"DateOnlyTimeOnlyParsing{(optimize ? "Release" : "Debug")}{target}",
                    source,
                    "DateOnlyTimeOnlyParsingFixture.EntryPoint",
                    optimize,
                    target,
                    42,
                    [])
                {
                    Exports = [new RequestedExport(
                        "run",
                        "DateOnlyTimeOnlyParsingFixture.EntryPoint",
                        "Run")],
                    WitPath = PlatformWitPath(assets),
                    WitWorld = "netwasm:platform/platform@1.0.0",
                });

                Assert.Equal(42, result);
            }
        }
    }

    [Theory(Skip = KnownNonBugSkipReasons.InvariantDateTimeGeneralGrammar)]
    [InlineData("February 2024")]
    [InlineData("2024/02")]
    [InlineData("02/2024")]
    [InlineData("29 2024 Feb")]
    [InlineData("February 2024 29")]
    [InlineData("Feb29 2024")]
    [InlineData("29Feb2024")]
    [InlineData("2024-02-29 AD")]
    [InlineData("AD 2024-02-29")]
    [InlineData("2024年2月29日")]
    [InlineData("February 99 1")]
    [InlineData("99 February 1")]
    [InlineData(",2024-2-29")]
    [InlineData("2024-2-29,")]
    public void DesktopDateOnlyGeneralGrammarOutsideTheInvariantProfile(string value) =>
        Assert.True(global::System.DateOnly.TryParse(
            value,
            global::System.Globalization.CultureInfo.InvariantCulture,
            global::System.Globalization.DateTimeStyles.None,
            out _));

    [Theory(Skip = KnownNonBugSkipReasons.InvariantDateTimeGeneralGrammar)]
    [InlineData("13:04.")]
    [InlineData("1 AM,")]
    [InlineData("1:02:03.4,PM")]
    public void DesktopTimeOnlyGeneralGrammarOutsideTheInvariantProfile(string value) =>
        Assert.True(global::System.TimeOnly.TryParse(
            value,
            global::System.Globalization.CultureInfo.InvariantCulture,
            global::System.Globalization.DateTimeStyles.None,
            out _));

    [Fact]
    public void CompilerFormatsInvariantDateOnlyAndTimeOnlyContracts()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            namespace DateOnlyTimeOnlyFormattingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var date = new global::System.DateOnly(2024, 2, 29);
                    if (date.ToString() != "02/29/2024" ||
                        date.ToString("d") != "02/29/2024" ||
                        date.ToString("D") != "Thursday, 29 February 2024" ||
                        date.ToString("M") != "February 29" ||
                        date.ToString("Y") != "2024 February" ||
                        date.ToString("O") != "2024-02-29" ||
                        date.ToString("R") != "Thu, 29 Feb 2024" ||
                        date.ToString("yyyyMMdd") != "20240229" ||
                        date.ToString("ddddd MMMMM yyyyyy") !=
                            "Thursday February 002024")
                    {
                        return 1;
                    }

                    var time = new global::System.TimeOnly(13, 4, 5, 120);
                    if (time.ToString() != "13:04" ||
                        time.ToString("t") != "13:04" ||
                        time.ToString("T") != "13:04:05" ||
                        time.ToString("R") != "13:04:05" ||
                        time.ToString("O") != "13:04:05.1200000" ||
                        time.ToString("HH-mm-ss.FFF") != "13-04-05.12" ||
                        time.ToString("HHH:mmm:sss ttt") != "13:04:05 PM")
                    {
                        return 2;
                    }

                    if (!ThrowsFormat(() => date.ToString("HH:mm")) ||
                        !ThrowsFormat(() => time.ToString("g")) ||
                        !ThrowsFormat(() => date.ToString("HH:mm", new ThrowingProvider())) ||
                        !ThrowsFormat(() => time.ToString("yyyy", new ThrowingProvider())))
                    {
                        return 3;
                    }

                    var provider = new CountingProvider();
                    _ = date.ToString("d", provider);
                    _ = date.ToString("O", provider);
                    _ = time.ToString("t", provider);
                    _ = time.ToString("R", provider);
                    if (provider.Calls != 2)
                    {
                        return 4;
                    }

                    global::System.Span<char> chars = stackalloc char[32];
                    chars.Fill('!');
                    if (date.TryFormat(chars[..9], out var charsWritten, "O") ||
                        charsWritten != 0 || chars[0] != '!')
                    {
                        return 5;
                    }

                    if (!date.TryFormat(chars, out charsWritten, "R", provider) ||
                        new string(chars[..charsWritten]) != "Thu, 29 Feb 2024" ||
                        provider.Calls != 2)
                    {
                        return 6;
                    }

                    if (!date.TryFormat(chars, out charsWritten, "ddddd MMMMM yyyyyy") ||
                        new string(chars[..charsWritten]) != "Thursday February 002024" ||
                        !time.TryFormat(chars, out charsWritten, "HHH:mmm:sss ttt") ||
                        new string(chars[..charsWritten]) != "13:04:05 PM")
                    {
                        return 12;
                    }

                    global::System.Span<byte> bytes = stackalloc byte[32];
                    if (!time.TryFormat(bytes, out var bytesWritten, "'λ'HH") ||
                        global::System.Text.Encoding.UTF8.GetString(bytes[..bytesWritten]) != "λ13")
                    {
                        return 7;
                    }

                    bytes.Fill((byte)'!');
                    if (time.TryFormat(bytes[..3], out bytesWritten, "'λ'HH") ||
                        bytesWritten != 0 || bytes[0] != (byte)'!')
                    {
                        return 8;
                    }

                    if (!time.TryFormat(bytes, out bytesWritten, "R", provider) ||
                        global::System.Text.Encoding.UTF8.GetString(bytes[..bytesWritten]) != "13:04:05" ||
                        provider.Calls != 2)
                    {
                        return 9;
                    }

                    if (!date.TryFormat(bytes, out bytesWritten, "ddddd MMMMM yyyyyy") ||
                        global::System.Text.Encoding.UTF8.GetString(bytes[..bytesWritten]) !=
                            "Thursday February 002024" ||
                        !time.TryFormat(bytes, out bytesWritten, "HHH:mmm:sss ttt") ||
                        global::System.Text.Encoding.UTF8.GetString(bytes[..bytesWritten]) !=
                            "13:04:05 PM")
                    {
                        return 13;
                    }

                    if (!date.TryFormat(chars, out charsWritten, "d", provider) ||
                        new string(chars[..charsWritten]) != "02/29/2024" ||
                        !time.TryFormat(chars, out charsWritten, "T", provider) ||
                        new string(chars[..charsWritten]) != "13:04:05" ||
                        provider.Calls != 4)
                    {
                        return 10;
                    }

                    try
                    {
                        _ = date.ToString("d", new ThrowingProvider());
                        return 11;
                    }
                    catch (global::System.InvalidOperationException)
                    {
                    }

                    try
                    {
                        _ = date.TryFormat(
                            chars,
                            out _,
                            "HH:mm",
                            new ThrowingProvider());
                        return 14;
                    }
                    catch (global::System.FormatException)
                    {
                    }

                    try
                    {
                        _ = time.TryFormat(
                            bytes,
                            out _,
                            "yyyy",
                            new ThrowingProvider());
                        return 15;
                    }
                    catch (global::System.FormatException)
                    {
                    }

                    return input;
                }

                private static bool ThrowsFormat(global::System.Func<string> action)
                {
                    try
                    {
                        _ = action();
                        return false;
                    }
                    catch (global::System.FormatException)
                    {
                        return true;
                    }
                }

                private sealed class CountingProvider : global::System.IFormatProvider
                {
                    public int Calls;

                    public object? GetFormat(global::System.Type? formatType)
                    {
                        Calls++;
                        return null;
                    }
                }

                private sealed class ThrowingProvider : global::System.IFormatProvider
                {
                    public object? GetFormat(global::System.Type? formatType) =>
                        throw new global::System.InvalidOperationException();
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                var result = executor.Execute(new CompilationScenario(
                    $"DateOnlyTimeOnlyFormatting{(optimize ? "Release" : "Debug")}{target}",
                    source,
                    "DateOnlyTimeOnlyFormattingFixture.EntryPoint",
                    optimize,
                    target,
                    42,
                    [])
                {
                    Exports = [new RequestedExport(
                        "run",
                        "DateOnlyTimeOnlyFormattingFixture.EntryPoint",
                        "Run")],
                    WitPath = PlatformWitPath(assets),
                    WitWorld = "netwasm:platform/platform@1.0.0",
                });

                Assert.Equal(42, result);
            }
        }
    }

    [Fact]
    public void CompilerPreservesDateOnlyAndTimeOnlyExceptionState()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            namespace DateOnlyTimeOnlyContractFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (!global::System.DateOnly.TryParse(s: "2024-02-29", out _) ||
                        !global::System.TimeOnly.TryParse(s: "13:04", out _) ||
                        !global::System.DateOnly.TryParseExact(
                            s: "2024-02-29", format: "O", out _))
                    {
                        return 1;
                    }

                    try
                    {
                        global::System.DateOnly.MinValue.AddMonths(-1);
                        return 2;
                    }
                    catch (global::System.ArgumentOutOfRangeException exception)
                    {
                        if (exception.ParamName != "months") return 2;
                    }

                    try
                    {
                        global::System.DateOnly.MinValue.AddYears(-1);
                        return 3;
                    }
                    catch (global::System.ArgumentOutOfRangeException exception)
                    {
                        if (exception.ParamName != "value") return 3;
                    }

                    try
                    {
                        global::System.DateOnly.FromDayNumber(-1);
                        return 4;
                    }
                    catch (global::System.ArgumentOutOfRangeException exception)
                    {
                        if (exception.ParamName != "dayNumber" ||
                            (int)exception.ActualValue! != -1) return 4;
                    }

                    try
                    {
                        global::System.DateOnly.MinValue.ToDateTime(
                            global::System.TimeOnly.MinValue,
                            (global::System.DateTimeKind)3);
                        return 5;
                    }
                    catch (global::System.ArgumentException exception)
                    {
                        if (exception.ParamName != "kind") return 5;
                    }

                    try
                    {
                        _ = new global::System.TimeOnly(0, 0, 0, 1000);
                        return 6;
                    }
                    catch (global::System.ArgumentOutOfRangeException exception)
                    {
                        if (exception.ParamName != "millisecond") return 6;
                    }

                    try
                    {
                        _ = new global::System.TimeOnly(0, 0, 0, 0, 1000);
                        return 7;
                    }
                    catch (global::System.ArgumentOutOfRangeException exception)
                    {
                        if (exception.ParamName != "microsecond") return 7;
                    }

                    if (global::System.DateOnly.TryParse(
                            s: null,
                            provider: null,
                            style: global::System.Globalization.DateTimeStyles.AssumeUniversal,
                            out _))
                    {
                        return 8;
                    }

                    try
                    {
                        global::System.DateOnly.TryParse(
                            default(global::System.ReadOnlySpan<char>),
                            null,
                            global::System.Globalization.DateTimeStyles.AssumeUniversal,
                            out _);
                        return 9;
                    }
                    catch (global::System.ArgumentException exception)
                    {
                        if (exception.ParamName != "style") return 9;
                    }

                    return 0;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                var result = executor.Execute(new CompilationScenario(
                    $"DateOnlyTimeOnlyContract{(optimize ? "Release" : "Debug")}{target}",
                    source,
                    "DateOnlyTimeOnlyContractFixture.EntryPoint",
                    optimize,
                    target,
                    0,
                    [])
                {
                    Exports = [new RequestedExport(
                        "run",
                        "DateOnlyTimeOnlyContractFixture.EntryPoint",
                        "Run")],
                    WitPath = PlatformWitPath(assets),
                    WitWorld = "netwasm:platform/platform@1.0.0",
                });

                Assert.Equal(0, result);
            }
        }
    }

    [Fact]
    public void CompilerFormatsRoundTripDateTextAcrossTargets()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            namespace DateTimeRoundTripFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    global::System.Span<byte> bytes = stackalloc byte[40];
                    var utc = new global::System.DateTime(
                        2020,
                        1,
                        2,
                        3,
                        4,
                        5,
                        678,
                        global::System.DateTimeKind.Utc);
                    if (!global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            utc,
                            bytes,
                            out var written,
                            new global::System.Buffers.StandardFormat('O'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "2020-01-02T03:04:05.6780000Z")
                    {
                        return -1;
                    }

                    if (!global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            utc,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('R'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "Thu, 02 Jan 2020 03:04:05 GMT"
                        || !global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            utc,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('l'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "thu, 02 jan 2020 03:04:05 gmt"
                        || !global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            utc,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('G'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "01/02/2020 03:04:05")
                    {
                        return -3;
                    }

                    var offset = new global::System.DateTimeOffset(
                        2020,
                        1,
                        2,
                        3,
                        4,
                        5,
                        678,
                        global::System.TimeSpan.FromMinutes(630));
                    if (!global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            offset,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('o'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "2020-01-02T03:04:05.6780000+10:30")
                    {
                        return -2;
                    }

                    if (!global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            offset,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('R'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "Wed, 01 Jan 2020 16:34:05 GMT"
                        || !global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            offset,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('l'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "wed, 01 jan 2020 16:34:05 gmt"
                        || !global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            offset,
                            bytes,
                            out written,
                            new global::System.Buffers.StandardFormat('G'))
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "01/02/2020 03:04:05"
                        || !global::System.Buffers.Text.Utf8Formatter.TryFormat(
                            offset,
                            bytes,
                            out written)
                        || global::System.Text.Encoding.UTF8.GetString(bytes[..written])
                            != "01/02/2020 03:04:05 +10:30")
                    {
                        return -4;
                    }

                    return input + 1;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                var result = executor.Execute(new CompilationScenario(
                    $"DateTimeRoundTrip{(optimize ? "Release" : "Debug")}{target}",
                    source,
                    "DateTimeRoundTripFixture.EntryPoint",
                    optimize,
                    target,
                    41,
                    [])
                {
                    Exports = [new RequestedExport(
                        "run",
                        "DateTimeRoundTripFixture.EntryPoint",
                        "Run")],
                    WitPath = PlatformWitPath(assets),
                    WitWorld = "netwasm:platform/platform@1.0.0",
                });

                Assert.Equal(42, result);
            }
        }
    }

    [Fact]
    public void CompilerPreservesStringsInReadonlyStructArrays()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ReadonlyStringStructArrayFixture",
            """
            namespace ReadonlyStringStructArrayFixture;

            public static class EntryPoint
            {
                private readonly struct Variable
                {
                    public Variable(string name, string value)
                    {
                        Name = name;
                        Value = value;
                    }

                    public string Name { get; }
                    public string Value { get; }
                }

                public static int Run(int input)
                {
                    var variables = new[] { new Variable("TZ", "Australia/Melbourne") };
                    return variables[0].Name == "TZ"
                        && variables[0].Value == "Australia/Melbourne"
                        ? input + 1
                        : -1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ReadonlyStringStructArrayFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerDecodesUtf8EnvironmentText()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "Utf8EnvironmentTextFixture",
            """
            namespace Utf8EnvironmentTextFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var nameBytes = new byte[] { 84, 90 };
                    var valueBytes = new byte[]
                    {
                        65, 117, 115, 116, 114, 97, 108, 105, 97, 47,
                        77, 101, 108, 98, 111, 117, 114, 110, 101,
                    };
                    var name = System.Text.Encoding.UTF8.GetString(nameBytes);
                    var value = System.Text.Encoding.UTF8.GetString(valueBytes);
                    return (nameBytes[0] == 84 && nameBytes[1] == 90 ? 1 : 0)
                        | (name.Length == 2 ? 2 : 0)
                        | (name == "TZ" ? 4 : 0)
                        | (value == "Australia/Melbourne" ? 8 : 0);
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Utf8EnvironmentTextFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(15, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPreservesMessagesFromStaticExceptionFactories()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StaticExceptionFactoryFixture",
            """
            namespace StaticExceptionFactoryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    try
                    {
                        throw Create("invalid payload");
                    }
                    catch (System.PlatformNotSupportedException exception)
                    {
                        return exception.Message == "validation: invalid payload"
                            ? input + 1
                            : -1;
                    }
                }

                private static System.PlatformNotSupportedException Create(string reason) =>
                    new("validation: " + reason);
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StaticExceptionFactoryFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPreservesNestedRefArgumentEvaluation()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "NestedRefArgumentFixture",
            """
            namespace NestedRefArgumentFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var offset = input;
                    return ReadText(ref offset, ReadLength(ref offset));
                }

                private static int ReadLength(ref int offset)
                {
                    offset += 2;
                    return 5;
                }

                private static int ReadText(ref int offset, int length) =>
                    offset * 10 + length;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NestedRefArgumentFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(35, ExecuteWithNode(result.ApplicationModule, assets.Directory, 1));
    }

    [Fact]
    public void CompilerRootsAllocatedConstructorArgumentsInValueArrays()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AllocatedConstructorArgumentFixture",
            """
            namespace AllocatedConstructorArgumentFixture;

            public static class EntryPoint
            {
                private readonly struct Variable
                {
                    public Variable(string name, string value)
                    {
                        Name = name;
                        Value = value;
                    }

                    public string Name { get; }
                    public string Value { get; }
                }

                public static int Run(int input)
                {
                    var variables = new Variable[1];
                    variables[0] = new Variable(
                        System.Text.Encoding.UTF8.GetString(new byte[] { 84, 90 }),
                        System.Text.Encoding.UTF8.GetString(new byte[]
                        {
                            65, 117, 115, 116, 114, 97, 108, 105, 97, 47,
                            77, 101, 108, 98, 111, 117, 114, 110, 101,
                        }));
                    return variables[0].Name == "TZ"
                        && variables[0].Value == "Australia/Melbourne"
                        ? input + 1
                        : -1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "AllocatedConstructorArgumentFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerMatchesDesktopForSeededGregorianAndOffsetCorpus()
    {
        var random = new Random(0x44617465);
        var cases = new List<(int Year, int Month, int Day, int DeltaMonths,
            int ExpectedYear, int ExpectedMonth, int ExpectedDay, int ExpectedDayOfWeek,
            long UnixSeconds, int OffsetMinutes, int ExpectedHour, int ExpectedMinute)>();
        for (var index = 0; index < 96; index++)
        {
            var year = random.Next(100, 9900);
            var month = random.Next(1, 13);
            var day = random.Next(1, System.DateTime.DaysInMonth(year, month) + 1);
            var deltaMonths = random.Next(-96, 97);
            var expectedDate = new System.DateOnly(year, month, day).AddMonths(deltaMonths);
            var unixSeconds = random.NextInt64(-2_208_988_800L, 4_102_444_800L);
            var offsetMinutes = random.Next(-56, 57) * 15;
            var expectedOffset = System.DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                .ToOffset(System.TimeSpan.FromMinutes(offsetMinutes));
            cases.Add((
                year,
                month,
                day,
                deltaMonths,
                expectedDate.Year,
                expectedDate.Month,
                expectedDate.Day,
                (int)expectedDate.DayOfWeek,
                unixSeconds,
                offsetMinutes,
                expectedOffset.Hour,
                expectedOffset.Minute));
        }

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DateTimeDifferentialFixture",
            BuildDateTimeDifferentialFixture(cases));
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DateTimeDifferentialFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Fact]
    public void CompilerBuildsPureDateTimeArithmeticForBothAddressWidthsWithoutClockImports()
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace DateTimeTargetFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var date = new System.DateTime(2000, 2, 29).AddYears(input);
                    return date.Month + date.Day;
                }
            }
            """;
        var assembly = assets.CompileSource("DateTimeTargetFixture", source);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var options = new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "DateTimeTargetFixture.EntryPoint",
                "Run",
                [],
                target);
            var first = NetWasmCompiler.Compile(options);
            var second = NetWasmCompiler.Compile(options);

            Assert.NotEmpty(first.ApplicationModule);
            Assert.Equal(target, first.Layouts.Target.Target);
            Assert.Empty(first.InteropManifest.Imports);
            Assert.Empty(first.Program.WitImportMethods);
            Assert.True(first.ApplicationModule.AsSpan().SequenceEqual(second.ApplicationModule));
        }
    }

    [Fact]
    public void CompilerRetainsOnlyTheReachableClockImport()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "UtcClockFixture",
            """
            namespace UtcClockFixture;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    (int)(System.DateTime.UtcNow.Ticks % 1000) + input;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "UtcClockFixture.EntryPoint",
            "Run",
            [],
            WitPath: PlatformWitPath(assets),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        var import = Assert.Single(result.Program.WitImportMethods);
        Assert.Equal(
            "wasi:clocks/wall-clock@0.2.11",
            import.WitImport!.InterfaceName);
        Assert.Equal("now", import.WitImport.FunctionName);
    }

    [Fact]
    public void CompilerExecutesUtcClockWithUtcLocalTimeFallback()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ClockFixture",
            """
            namespace ClockFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var utc = System.DateTime.UtcNow;
                    var local = System.DateTime.Now;
                    var offset = System.DateTimeOffset.Now;
                    return utc.Kind == System.DateTimeKind.Utc
                        && utc.Ticks % System.TimeSpan.TicksPerSecond == 1230000
                        && local.Kind == System.DateTimeKind.Local
                        && local.Ticks == utc.Ticks
                        && offset.Offset == System.TimeSpan.Zero
                        && offset.UtcTicks == utc.Ticks
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ClockFixture.EntryPoint",
            "Run",
            [new RequestedExport("run", "ClockFixture.EntryPoint", "Run")],
            WitPath: PlatformWitPath(assets),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Contains(result.Program.WitImportMethods,
            method => method.WitImport!.InterfaceName ==
                "wasi:clocks/wall-clock@0.2.11" &&
                method.WitImport.FunctionName == "now");
        Assert.DoesNotContain(result.Program.WitImportMethods,
            method => method.WitImport!.InterfaceName.StartsWith(
                "netwasm:timezone", StringComparison.Ordinal));
        Assert.Equal(42, ExecuteWithNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            expectedEnvironmentReadsBeforeRun: 1));
    }

    [Fact]
    public void CompilerExecutesNamedLocalZoneAcrossCilAndAddressWidths()
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace NamedLocalTimeFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var local = System.DateTime.Now;
                    var offset = System.DateTimeOffset.Now;
                    return (local.Kind == System.DateTimeKind.Local ? 1 : 0)
                        | (local.Year == 1970 && local.Month == 1 && local.Day == 1 ? 2 : 0)
                        | (local.Hour == 10 ? 4 : 0)
                        | (offset.Offset == System.TimeSpan.FromHours(10) ? 8 : 0)
                        | (offset.UtcTicks == System.DateTime.UnixEpoch.Ticks + 1230000 ? 16 : 0);
                }
            }
            """;
        var assetPath = Path.Combine(assets.Directory, "netwasm-timezones.nwtz");
        File.WriteAllBytes(assetPath, BuildTimeZoneAsset(
            "2026c",
            "Australia/Melbourne",
            10 * 60 * 60));

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var environment = ImmutableDictionary<string, string>.Empty.Add(
            "TZ",
            "Australia/Melbourne");
        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                var result = executor.Execute(new CompilationScenario(
                    optimize ? "NamedLocalTimeReleaseFixture" : "NamedLocalTimeDebugFixture",
                    source,
                    "NamedLocalTimeFixture.EntryPoint",
                    optimize,
                    target,
                    41,
                    [])
                {
                    Exports = [new RequestedExport(
                        "run",
                        "NamedLocalTimeFixture.EntryPoint",
                        "Run")],
                    Environment = environment,
                    TimeZoneAssetPath = assetPath,
                    WitPath = PlatformWitPath(assets),
                    WitWorld = "netwasm:platform/platform@1.0.0",
                });

                Assert.Equal(31, result);
            }
        }
    }

    [Fact]
    public void CompilerExecutesDateTimeTextInDebugAndOptimizedCil()
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace DateTimeOptimizationFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = System.DateTimeOffset.Parse(
                        "2000-02-29T12:34:56.7654321-03:30");
                    return value.Year == 2000
                        && value.Offset == System.TimeSpan.FromMinutes(-210)
                        && value.ToUnixTimeSeconds() == 951840296
                        ? input + 1
                        : -1;
                }
            }
            """;
        var assemblies = new[]
        {
            assets.CompileSource("DateTimeOptimizationDebugFixture", source),
            assets.CompileOptimizedSource("DateTimeOptimizationReleaseFixture", source),
        };

        foreach (var assembly in assemblies)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "DateTimeOptimizationFixture.EntryPoint",
                "Run",
                []));
            Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        }
    }

    private static byte[] BuildTimeZoneAsset(
        string version,
        string name,
        int initialOffsetSeconds)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new byte[] { 0x4e, 0x57, 0x54, 0x5a, 1, 0, 0, 0 });
            WriteText(writer, version);
            writer.Write(1);
            WriteText(writer, name);
            writer.Write(initialOffsetSeconds);
            writer.Write(false);
            writer.Write(0);
        }
        var bytes = payload.ToArray();
        return [.. bytes, .. SHA256.HashData(bytes)];

        static void WriteText(BinaryWriter writer, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            writer.Write(checked((ushort)bytes.Length));
            writer.Write(bytes);
        }
    }

    [Fact]
    public void CompilerExecutesInvariantDateTimeTextRoundTrips()
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            namespace DateTimeTextFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var span = System.TimeSpan.Parse("-2.03:04:05.0060070");
                    var date = System.DateOnly.Parse("2000-02-29");
                    var time = System.TimeOnly.Parse("23:59:58.1234567");
                    var utc = System.DateTime.Parse("2024-02-29T23:59:58.1234567Z");
                    var offset = System.DateTimeOffset.Parse(
                        "2024-02-29T23:59:58.1234567+10:30");
                    var minimumSpan = System.TimeSpan.Parse("-10675199.02:48:05.4775808");
                    var combined = date.ToDateTime(time);
                    return span.ToString() == "-2.03:04:05.0060070"
                        && minimumSpan == System.TimeSpan.MinValue
                        && minimumSpan.ToString() == "-10675199.02:48:05.4775808"
                        && date.ToString() == "02/29/2000"
                        && time.ToString() == "23:59"
                        && System.DateOnly.FromDateTime(combined) == date
                        && System.TimeOnly.FromDateTime(combined) == time
                        && utc.ToString() == "02/29/2024 23:59:58"
                        && offset.ToString() == "02/29/2024 23:59:58 +10:30"
                        && offset.UtcDateTime.ToString() == "02/29/2024 13:29:58"
                        ? input + 1
                        : -1;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "DateTimeTextFixture",
            source,
            "DateTimeTextFixture.EntryPoint",
            Optimize: false,
            WasmTarget.Wasm32,
            41,
            [])
        {
            Exports = [new RequestedExport(
                "run",
                "DateTimeTextFixture.EntryPoint",
                "Run")],
            WitPath = PlatformWitPath(assets),
            WitWorld = "netwasm:platform/platform@1.0.0",
        });

        Assert.Equal(42, result);
    }

    [Fact]
    public void CompilerExecutesDateTimeTryParseAndManagedFailures()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DateTimeFailureFixture",
            """
            namespace DateTimeFailureFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var invalid = !System.TimeSpan.TryParse("24:00:00", out _)
                        && !System.DateOnly.TryParse("2023-02-29", out _)
                        && !System.TimeOnly.TryParse("12:60:00", out _)
                        && !System.DateTime.TryParse("2024-01-01 00:00:00", out _)
                        && !System.DateTimeOffset.TryParse(
                            "2024-01-01T00:00:00+14:01", out _);
                    var format = 0;
                    try { _ = System.DateOnly.Parse("not-a-date"); }
                    catch (System.FormatException) { format++; }
                    try { _ = System.TimeOnly.Parse("25:00"); }
                    catch (System.FormatException) { format++; }
                    try { _ = System.DateTime.Parse("0000-01-01T00:00:00"); }
                    catch (System.FormatException) { format++; }
                    try { _ = System.DateTimeOffset.Parse("2024-01-01T00:00:00+99:00"); }
                    catch (System.FormatException) { format++; }
                    return invalid && format == 4 ? input + 1 : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DateTimeFailureFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesPreEpochFlooringAndRangeFailures()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DateTimeRangeFixture",
            """
            namespace DateTimeRangeFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var beforeEpoch = new System.DateTimeOffset(
                        new System.DateTime(1969, 12, 31).Ticks
                            + System.TimeSpan.TicksPerDay - 1,
                        System.TimeSpan.Zero);
                    var range = 0;
                    try { _ = System.DateTimeOffset.FromUnixTimeSeconds(long.MaxValue); }
                    catch (System.ArgumentOutOfRangeException) { range++; }
                    try { _ = new System.DateTimeOffset(
                        System.DateTime.MinValue.Ticks,
                        System.TimeSpan.FromHours(1)); }
                    catch (System.ArgumentOutOfRangeException) { range++; }
                    try { _ = new System.DateTimeOffset(
                        System.DateTime.MinValue.Ticks,
                        System.TimeSpan.FromSeconds(1)); }
                    catch (System.ArgumentException) { range++; }
                    return beforeEpoch.ToUnixTimeSeconds() == -1
                        && beforeEpoch.ToUnixTimeMilliseconds() == -1
                        && range == 3
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DateTimeRangeFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesDateTimeAndOffsetEpochArithmetic()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DateTimeOffsetFixture",
            """
            namespace DateTimeOffsetFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var leap = new System.DateTime(2024, 2, 29, 23, 59, 30);
                    var next = leap.AddSeconds(31);
                    var offset = new System.DateTimeOffset(
                        next.Ticks,
                        System.TimeSpan.FromHours(10));
                    var utc = offset.ToOffset(System.TimeSpan.Zero);
                    var epoch = System.DateTimeOffset.FromUnixTimeSeconds(input);
                    return next.Year == 2024
                        && next.Month == 3
                        && next.Day == 1
                        && next.Hour == 0
                        && next.Second == 1
                        && utc.Hour == 14
                        && epoch.ToUnixTimeSeconds() == input
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DateTimeOffsetFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesDateOnlyAndTimeOnlyCalendarBoundaries()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "DateAndTimeOnlyFixture",
            """
            namespace DateAndTimeOnlyFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var leap = new System.DateOnly(2024, 2, 29);
                    var nextYear = leap.AddYears(1);
                    var time = new System.TimeOnly(23, 59, 30).AddMinutes(2);
                    var parsed = System.DateOnly.Parse("1970-01-01");
                    var morning = new System.TimeOnly(8, 0);
                    var noon = new System.TimeOnly(12, 0);
                    var overnightStart = new System.TimeOnly(23, 0);
                    var overnightEnd = new System.TimeOnly(1, 0);
                    return nextYear.Year == 2025
                        && nextYear.Month == 2
                        && nextYear.Day == 28
                        && time.Hour == 0
                        && time.Minute == 1
                        && time.Second == 30
                        && parsed.DayNumber == 719162
                        && leap.DayOfWeek == System.DayOfWeek.Thursday
                        && new System.TimeOnly(10, 0).IsBetween(morning, noon)
                        && !new System.TimeOnly(7, 0).IsBetween(morning, noon)
                        && new System.TimeOnly(23, 30).IsBetween(overnightStart, overnightEnd)
                        && new System.TimeOnly(0, 30).IsBetween(overnightStart, overnightEnd)
                        && !new System.TimeOnly(12, 0).IsBetween(overnightStart, overnightEnd)
                        && !morning.IsBetween(morning, morning)
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DateAndTimeOnlyFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesTimeSpanArithmeticComponentsAndOverflow()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "TimeSpanFixture",
            """
            namespace TimeSpanFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new System.TimeSpan(1, 2, 3, 4, 5);
                    var adjusted = value + System.TimeSpan.FromSeconds(input);
                    var overflow = false;
                    try
                    {
                        _ = System.TimeSpan.MaxValue + System.TimeSpan.FromTicks(1);
                    }
                    catch (System.OverflowException)
                    {
                        overflow = true;
                    }
                    return overflow
                        && adjusted.Days == 1
                        && adjusted.Hours == 2
                        && adjusted.Minutes == 3
                        && adjusted.Seconds == 45
                        && adjusted.Milliseconds == 5
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "TimeSpanFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    private static string BuildDateTimeDifferentialFixture(
        IReadOnlyList<(int Year, int Month, int Day, int DeltaMonths,
            int ExpectedYear, int ExpectedMonth, int ExpectedDay, int ExpectedDayOfWeek,
            long UnixSeconds, int OffsetMinutes, int ExpectedHour, int ExpectedMinute)> cases)
    {
        var rows = string.Join(
            ",\n",
            cases.Select(item =>
                $"new Case({item.Year}, {item.Month}, {item.Day}, {item.DeltaMonths}, " +
                $"{item.ExpectedYear}, {item.ExpectedMonth}, {item.ExpectedDay}, " +
                $"{item.ExpectedDayOfWeek}, {item.UnixSeconds}L, {item.OffsetMinutes}, " +
                $"{item.ExpectedHour}, {item.ExpectedMinute})"));
        return $$"""
            namespace DateTimeDifferentialFixture;

            public readonly struct Case
            {
                public Case(int year, int month, int day, int deltaMonths,
                    int expectedYear, int expectedMonth, int expectedDay,
                    int expectedDayOfWeek, long unixSeconds, int offsetMinutes,
                    int expectedHour, int expectedMinute)
                {
                    Year = year; Month = month; Day = day; DeltaMonths = deltaMonths;
                    ExpectedYear = expectedYear; ExpectedMonth = expectedMonth;
                    ExpectedDay = expectedDay; ExpectedDayOfWeek = expectedDayOfWeek;
                    UnixSeconds = unixSeconds; OffsetMinutes = offsetMinutes;
                    ExpectedHour = expectedHour; ExpectedMinute = expectedMinute;
                }
                public int Year { get; }
                public int Month { get; }
                public int Day { get; }
                public int DeltaMonths { get; }
                public int ExpectedYear { get; }
                public int ExpectedMonth { get; }
                public int ExpectedDay { get; }
                public int ExpectedDayOfWeek { get; }
                public long UnixSeconds { get; }
                public int OffsetMinutes { get; }
                public int ExpectedHour { get; }
                public int ExpectedMinute { get; }
            }

            public static class EntryPoint
            {
                private static readonly Case[] Cases =
                [
                    {{rows}}
                ];

                public static int Run(int input)
                {
                    for (var index = 0; index < Cases.Length; index++)
                    {
                        var item = Cases[index];
                        var date = new System.DateOnly(item.Year, item.Month, item.Day)
                            .AddMonths(item.DeltaMonths);
                        if (date.Year != item.ExpectedYear
                            || date.Month != item.ExpectedMonth
                            || date.Day != item.ExpectedDay
                            || (int)date.DayOfWeek != item.ExpectedDayOfWeek)
                            return index + 1;
                        var offset = System.DateTimeOffset
                            .FromUnixTimeSeconds(item.UnixSeconds)
                            .ToOffset(System.TimeSpan.FromMinutes(item.OffsetMinutes));
                        if (offset.ToUnixTimeSeconds() != item.UnixSeconds
                            || offset.Hour != item.ExpectedHour
                            || offset.Minute != item.ExpectedMinute)
                            return index + 1001;
                    }
                    return input;
                }
            }
            """;
    }

    private static string PlatformWitPath(TestAssets assets) => Path.Combine(
        assets.Root,
        "wit",
        "netwasm-platform-1.0.0");
}
