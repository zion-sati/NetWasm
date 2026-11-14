using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CurrentCultureFlowFormattingCompilationTests
{
    private static readonly string[] CultureMachinery =
    [
        "System.Globalization.CultureInfo",
        "System.Globalization.CultureData",
    ];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CurrentNumberFormatFollowsTheCultureThroughAsyncFlow(bool optimize, WasmTarget target)
    {
        // Formatting without a provider reads NumberFormatInfo.CurrentInfo, which NetWasm mirrors from the
        // current culture. The culture flows with the execution context, so a culture set inside an async
        // method applies to its continuation and does not leak back to the caller, as on desktop .NET.
        const string source = """
            using System;
            using System.Globalization;
            using System.Threading.Tasks;
            namespace CultureFlowFixture;
            public static class EntryPoint
            {
                private static readonly TaskCompletionSource<bool> Resume = new();

                private static async Task<string> FormatAfterResumingAsync(CultureInfo culture)
                {
                    CultureInfo.CurrentCulture = culture;
                    var before = (-1.5).ToString();
                    await Resume.Task;
                    return before + "|" + (-1.5).ToString() + "|" +
                        (ReferenceEquals(NumberFormatInfo.CurrentInfo, CultureInfo.CurrentCulture.NumberFormat) ? "same" : "different");
                }

                public static int Run(int value)
                {
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                    custom.NumberFormat.NegativeSign = "~";
                    custom.NumberFormat.NumberDecimalSeparator = ",";

                    var pending = FormatAfterResumingAsync(custom);
                    if ((-1.5).ToString() != "-1.5") return 1;
                    if (!ReferenceEquals(CultureInfo.CurrentCulture, CultureInfo.InvariantCulture)) return 2;

                    // The continuation runs inline on SetResult, inside the async method's own context.
                    Resume.SetResult(true);
                    if (!pending.IsCompletedSuccessfully) return 3;
                    if (pending.Result != "~1,5|~1,5|same") return 4;
                    if ((-1.5).ToString() != "-1.5" || 2.5m.ToString() != "2.5") return 5;
                    return value + 1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "CultureFlowFixture", source, optimize, target);

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    // The dates' parameterless ToString is the invariant general text while no culture is set, so it reaches no
    // culture machinery either. A date formatted with a format string does: upstream's DateTimeFormat reads
    // DateTimeFormatInfo for every format but the ones it recognizes at run time.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DefaultToStringOfCommonValueTypesDoesNotReachCultureMachinery(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            namespace DefaultToStringFixture;
            public enum Color { Red, Green }
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    Console.WriteLine(new DateTime(2024, 2, 29, 13, 4, value % 60).ToString());
                    Console.WriteLine(new DateTimeOffset(2024, 2, 29, 13, 4, 5, new TimeSpan(-5, -30, 0)).ToString());
                    Console.WriteLine(new DateOnly(2024, 2, 29).AddDays(value).ToString());
                    Console.WriteLine(new TimeOnly(13, 4).Add(TimeSpan.FromSeconds(value)).ToString());
                    Console.WriteLine(TimeSpan.FromSeconds(value).ToString());
                    Console.WriteLine(new Guid(value, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10).ToString());
                    Console.WriteLine((value > 0).ToString());
                    Console.WriteLine(((char)('A' + value % 26)).ToString());
                    Console.WriteLine(((Color)(value % 2)).ToString());
                    return value + 1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "DefaultToStringFixture", source, optimize, target);
        var reachable = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(reachable, CultureMachinery.Contains);
        Assert.Equal(
            "02/29/2024 13:04:41\n02/29/2024 13:04:05 -05:30\n04/10/2024\n13:04\n00:00:41\n00000029-0001-0002-0304-05060708090a\nTrue\nP\nGreen\n",
            CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(
                result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    private static CompilationResult Compile(TestAssets assets, string name, string source, bool optimize, WasmTarget target)
    {
        var assembly = optimize ? assets.CompileOptimizedSource(name, source) : assets.CompileSource(name, source);
        return NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], $"{name}.EntryPoint", "Run",
            [new("run", $"{name}.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
    }
}
