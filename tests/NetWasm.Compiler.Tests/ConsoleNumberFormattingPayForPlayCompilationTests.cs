using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConsoleNumberFormattingPayForPlayCompilationTests
{
    private static readonly string[] CultureMachinery =
    [
        "System.Globalization.CultureInfo",
        "System.Globalization.CultureData",
        "System.Globalization.DateTimeFormatInfo",
        "System.Globalization.Calendar",
        "System.Globalization.GregorianCalendar",
    ];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConsoleAndDefaultNumberFormattingDoNotReachCultureMachinery(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            namespace ConsoleNumberFixture;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    Console.WriteLine(value);
                    Console.WriteLine(-value);
                    Console.WriteLine((long)value * 3);
                    Console.WriteLine((ulong)value);
                    Console.WriteLine($"{value}:{-value}");
                    Console.WriteLine(value.ToString("N2"));
                    Console.WriteLine(value + 0.5);
                    Console.WriteLine(-value - 0.25f);
                    Console.WriteLine(value + 0.75m);
                    Console.WriteLine((value / 3.0).ToString("F2"));
                    return value + 1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "ConsoleNumberFixture", source, optimize, target);
        var reachable = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(reachable, CultureMachinery.Contains);
        Assert.DoesNotContain(NetWasmRuntimeFeatureIds.ArraySetValue, result.RuntimeFeatures);
        Assert.DoesNotContain(NetWasmRuntimeFeatureIds.ObjectClone, result.RuntimeFeatures);
        Assert.Equal("41\n-41\n123\n41\n41:-41\n41.00\n41.5\n-41.25\n41.75\n13.67\n", CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ConsoleNumbersFollowACustomizedCurrentCulture(bool optimize, WasmTarget target)
    {
        // The console formats integers with a null provider; NumberFormatInfo.CurrentInfo must
        // track the current culture exactly as TextWriter.FormatProvider would.
        const string source = """
            using System;
            using System.Globalization;
            namespace ConsoleCultureFixture;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                    culture.NumberFormat.NegativeSign = "~";
                    culture.NumberFormat.NumberDecimalSeparator = ",";
                    CultureInfo.CurrentCulture = culture;
                    Console.WriteLine(-value);
                    Console.WriteLine(-(long)value * 3);
                    Console.WriteLine(-value - 0.5);
                    Console.WriteLine(-value - 0.25m);
                    culture.NumberFormat.NegativeSign = "!";
                    Console.WriteLine(-1);
                    var provider = Console.Out.FormatProvider;
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    Console.WriteLine(-1);
                    return ReferenceEquals(provider, culture) ? value + 1 : 0;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "ConsoleCultureFixture", source, optimize, target);

        Assert.Equal("~41\n~123\n~41,5\n~41,25\n!1\n-1\n", CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CloningAndBoxedArrayStoresRecordTheirRuntimeFeatures(bool optimize, WasmTarget target)
    {
        const string source = """
            namespace RuntimeFeatureFixture;
            public sealed class Box
            {
                public int Value;
                public Box Copy() => (Box)MemberwiseClone();
            }
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var array = new int[1];
                    array.SetValue((object)value, 0);
                    var copy = new Box { Value = value }.Copy();
                    return array[0] == value && copy.Value == value ? value + 1 : 0;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var result = Compile(assets, "RuntimeFeatureFixture", source, optimize, target);

        Assert.Contains(NetWasmRuntimeFeatureIds.ArraySetValue, result.RuntimeFeatures);
        Assert.Contains(NetWasmRuntimeFeatureIds.ObjectClone, result.RuntimeFeatures);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
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
