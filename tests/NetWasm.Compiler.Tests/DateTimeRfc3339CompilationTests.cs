using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DateTimeRfc3339CompilationTests
{
    [Theory]
    [MemberData(nameof(AsyncValueTaskResumptionCompilationTests.Configurations),
        MemberType = typeof(AsyncValueTaskResumptionCompilationTests))]
    [Trait("Issue", "62")]
    public void InvariantRfc3339FormattingAndParsingMatchDesktop(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "DateTimeRfc3339Fixture", Source, "DateTimeRfc3339Fixture.EntryPoint",
            optimize, target, 42, [])
        {
            Exports = [new RequestedExport(
                "run",
                "DateTimeRfc3339Fixture.EntryPoint",
                "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
        });
        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        using System.Globalization;

        namespace DateTimeRfc3339Fixture;

        public static class EntryPoint
        {
            private const string Pattern = "yyyy-MM-ddTHH:mm:ss.FFFFFFFK";
            private const string UtcText = "2024-03-05T12:30:45Z";

            public static int Run(int input)
            {
                for (var index = 0; index < 18; index++)
                {
                    try
                    {
                        if (!Check(index)) return index + 1;
                    }
                    catch (Exception)
                    {
                        return index + 101;
                    }
                }
                return input;
            }

            private static bool Check(int index)
            {
                var provider = CultureInfo.InvariantCulture;
                var value = new DateTimeOffset(2024, 3, 5, 12, 30, 45, TimeSpan.Zero);
                switch (index)
                {
                    case 0:
                        return value.UtcDateTime.ToString(Pattern, provider) == UtcText;
                    case 1:
                        return value.AddTicks(1234000).UtcDateTime.ToString(Pattern, provider)
                            == "2024-03-05T12:30:45.1234Z";
                    case 2:
                        return new DateTimeOffset(2024, 3, 5, 12, 30, 45,
                            TimeSpan.FromMinutes(330)).ToString(Pattern, provider)
                            == "2024-03-05T12:30:45+05:30";
                    case 3:
                        var buffer = new char[64];
                        return value.UtcDateTime.TryFormat(buffer, out var written, Pattern, provider)
                            && new string(buffer, 0, written) == UtcText;
                    case 4:
                        return ParseMatches(UtcText, value, DateTimeStyles.None);
                    case 5:
                        return ParseMatches(UtcText, value, DateTimeStyles.RoundtripKind);
                    case 6:
                        return ParseMatches("2024-03-05T12:30:45.1234567+05:30",
                            new DateTimeOffset(2024, 3, 5, 12, 30, 45,
                                TimeSpan.FromMinutes(330)).AddTicks(1234567),
                            DateTimeStyles.RoundtripKind);
                    case 7:
                        return ParseMatches("2024-03-05T12:30:45.123-03:30",
                            new DateTimeOffset(2024, 3, 5, 12, 30, 45,
                                TimeSpan.FromMinutes(-210)).AddTicks(1230000),
                            DateTimeStyles.RoundtripKind);
                    case 8:
                        return DateTimeOffset.TryParseExact(UtcText.AsSpan(), Pattern.AsSpan(), provider,
                            DateTimeStyles.RoundtripKind, out var spanResult)
                            && spanResult.EqualsExact(value);
                    case 9:
                        return DateTimeOffset.TryParseExact(UtcText, new[] { Pattern }, provider,
                            DateTimeStyles.RoundtripKind, out var arrayResult)
                            && arrayResult.EqualsExact(value);
                    case 10:
                        return DateTimeOffset.ParseExact(UtcText, Pattern, provider,
                            DateTimeStyles.RoundtripKind).EqualsExact(value);
                    case 11:
                        return DateTimeOffset.TryParse(UtcText, provider, DateTimeStyles.None,
                            out var generalResult) && generalResult.EqualsExact(value);
                    case 12:
                        return DateTimeOffset.TryParseExact(
                            "2024-03-05T12:30:45.0000000+00:00", "O", provider,
                            DateTimeStyles.None, out var roundtripResult)
                            && roundtripResult.EqualsExact(value);
                    case 13:
                        return !DateTimeOffset.TryParseExact("2024-03-05T12:30:45+00:00", "O",
                            provider, DateTimeStyles.None, out _);
                    case 14:
                        return !DateTimeOffset.TryParseExact("2024-02-30T12:30:45Z", Pattern,
                            provider, DateTimeStyles.RoundtripKind, out _);
                    case 15:
                        return value.UtcDateTime.ToString("O", provider)
                            == "2024-03-05T12:30:45.0000000Z";
                    case 16:
                        return DateTimeOffset.TryParse("2024-03-05T12:30:45+00:00", provider,
                            DateTimeStyles.None, out var numericResult)
                            && numericResult.EqualsExact(value);
                    case 17:
                        try { _ = value.UtcDateTime.ToString("yyyy-MM-dd'", provider); }
                        catch (FormatException) { return true; }
                        return false;
                    default:
                        return false;
                }
            }

            private static bool ParseMatches(
                string text,
                DateTimeOffset expected,
                DateTimeStyles styles) =>
                DateTimeOffset.TryParseExact(text, Pattern, CultureInfo.InvariantCulture, styles,
                    out var actual) && actual.EqualsExact(expected);
        }
        """;
}
