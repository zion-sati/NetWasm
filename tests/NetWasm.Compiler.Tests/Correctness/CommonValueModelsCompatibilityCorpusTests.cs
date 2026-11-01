namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CommonValueModelsCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CommonValueModelsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "CommonValueModelsCompatibilityCorpus",
            "NetWasm.Correctness.CommonValueModels",
            """
            using System;
            using System.Diagnostics;
            using System.Runtime.Versioning;

            namespace NetWasm.Correctness.CommonValueModels;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    return input switch
                    {
                        1 => ExerciseTimeSpan(),
                        2 => ExerciseDateTime(),
                        3 => ExerciseDateTimeOffset(),
                        4 => ExerciseDateOnlyAndTimeOnly(),
                        5 => ExerciseGuid(),
                        6 => ExerciseVersion(),
                        7 => ExerciseFrameworkName(),
                        8 => ExerciseAppContext(),
                        9 => ExerciseConvert(),
                        10 => ExerciseStopwatchMath(),
                        11 => ExerciseTimeSpanParse(),
                        _ => -1,
                    };
                }

                private static int ExerciseTimeSpan()
                {
                    var value = new TimeSpan(1, 2, 3, 4, 5, 6)
                        .Add(TimeSpan.FromMinutes(7));
                    var text = value.ToString();
                    return text == "1.02:10:04.0050060"
                        ? value.Days * 10000 + value.Hours * 1000 +
                            value.Minutes * 100 + value.Seconds * 10 +
                            value.Milliseconds
                        : -1;
                }

                private static int ExerciseTimeSpanParse()
                {
                    var parsed = TimeSpan.Parse("1.02:10:04.0050060");
                    return parsed.Days * 10000 + parsed.Hours * 1000 +
                        parsed.Minutes * 100 + parsed.Seconds * 10 +
                        parsed.Milliseconds;
                }

                private static int ExerciseDateTime()
                {
                    var value = new DateTime(
                        2024, 2, 28, 23, 59, 58, 123, 456,
                        DateTimeKind.Utc).AddDays(1).AddSeconds(3);
                    var parsed = DateTime.Parse(value.ToString());
                    return parsed.Year + parsed.Month * 10 + parsed.Day * 100 +
                        parsed.Hour * 1000 + parsed.Second * 10000;
                }

                private static int ExerciseDateTimeOffset()
                {
                    var value = new DateTimeOffset(
                        2020, 6, 7, 8, 9, 10, 11,
                        TimeSpan.FromHours(2));
                    var roundTrip = DateTimeOffset.FromUnixTimeSeconds(
                        value.ToUnixTimeSeconds());
                    return roundTrip.Year * 100 + roundTrip.Month * 10 +
                        roundTrip.Day;
                }

                private static int ExerciseDateOnlyAndTimeOnly()
                {
                    var date = DateOnly.Parse("2024-02-29").AddYears(1);
                    var time = TimeOnly.Parse("23:58:57.1234567").AddMinutes(3);
                    return date.DayNumber + time.Hour * 1000 + time.Minute * 100;
                }

                private static int ExerciseGuid()
                {
                    var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
                    var bytes = value.ToByteArray();
                    var roundTrip = new Guid(bytes);
                    if (roundTrip != value)
                    {
                        return -2;
                    }
                    if (roundTrip.ToString("N") !=
                        "00112233445566778899aabbccddeeff")
                    {
                        return -3;
                    }
                    return bytes[0] * 100 + bytes[15];
                }

                private static int ExerciseVersion()
                {
                    var value = Version.Parse("10.2.30.4");
                    var other = new Version(10, 2, 30, 5);
                    return value.CompareTo(other) < 0
                        ? value.Major * 1000 + value.Minor * 100 +
                            value.Build * 10 + value.Revision
                        : -1;
                }

                private static int ExerciseFrameworkName()
                {
                    var value = new FrameworkName("NetWasm,Version=v1.2,Profile=Core");
                    return value.Identifier.Length * 100 +
                        value.Version.Major * 10 + value.Version.Minor;
                }

                private static int ExerciseAppContext()
                {
                    if (AppContext.GetData("missing") is not null ||
                        AppContext.TryGetSwitch("missing-switch", out _))
                    {
                        return -1;
                    }

                    AppContext.SetData("model", 42);
                    AppContext.SetData("text-feature", "true");
                    if (!AppContext.TryGetSwitch("text-feature", out var textEnabled) ||
                        !textEnabled)
                    {
                        return -2;
                    }

                    AppContext.SetSwitch("text-feature", false);
                    AppContext.SetSwitch("feature", true);
                    return (int)AppContext.GetData("model")! +
                        (AppContext.TryGetSwitch("feature", out var enabled) && enabled
                            ? 100
                            : 0) +
                        (AppContext.TryGetSwitch("text-feature", out textEnabled) && !textEnabled
                            ? 1
                            : 0);
                }

                private static int ExerciseConvert()
                {
                    var value = new DateTime(2023, 4, 5);
                    return Convert.ToDateTime(value).Day * 100 +
                        (Convert.ToString(value).Contains("2023") ? 1 : 0);
                }

                private static int ExerciseStopwatchMath()
                {
                    var elapsed = Stopwatch.GetElapsedTime(1_000, 2_501);
                    return (int)elapsed.Ticks;
                }

                public static int Trace() => 0;
            }
            """,
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
        {
            CaptureCompilerDiagnostics = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
