using System.Collections.Immutable;
using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class DateTimeCompilationTests
{
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
                    WitWorld = "netwasm:platform@1.0.0/platform",
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
            WitWorld: "netwasm:platform@1.0.0/platform"));

        var import = Assert.Single(result.Program.WitImportMethods);
        Assert.Equal(
            "wasi:clocks@0.2.11/wall-clock",
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
            WitWorld: "netwasm:platform@1.0.0/platform"));

        Assert.Contains(result.Program.WitImportMethods,
            method => method.WitImport!.InterfaceName ==
                "wasi:clocks@0.2.11/wall-clock" &&
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
                    WitWorld = "netwasm:platform@1.0.0/platform",
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
        var assembly = assets.CompileSource(
            "DateTimeTextFixture",
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
                        && date.ToString() == "2000-02-29"
                        && time.ToString() == "23:59:58.1234567"
                        && System.DateOnly.FromDateTime(combined) == date
                        && System.TimeOnly.FromDateTime(combined) == time
                        && utc.ToString() == "2024-02-29T23:59:58.1234567Z"
                        && offset.ToString() == "2024-02-29T23:59:58.1234567+10:30"
                        && offset.UtcDateTime.ToString() == "2024-02-29T13:29:58.1234567Z"
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "DateTimeTextFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
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
                    return nextYear.Year == 2025
                        && nextYear.Month == 2
                        && nextYear.Day == 28
                        && time.Hour == 0
                        && time.Minute == 1
                        && time.Second == 30
                        && parsed.DayNumber == 719162
                        && leap.DayOfWeek == System.DayOfWeek.Thursday
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
