using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A tuple an inlined method builds and its caller deconstructs becomes the
// caller's locals, an int pair and a mixed triple alike, so the caller needs no
// value frame. A tuple that also goes whole to a call keeps its storage, and
// every result matches desktop.
public sealed partial class TupleDeconstructionCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static (int Sum, int Difference) Pair(int a, int b) => (a + b, a - b);

            private static (double Scaled, long Shifted, int Mixed) Triple(double x, int k) => (x * k, (long)k << 33, k ^ 5);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sink((int, int) pair) => pair.Item1 * 7 + pair.Item2;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Ints(int a, int b)
            {
                var (sum, difference) = Pair(a, b);
                return sum * 31 + difference;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Mixed(double x, int k)
            {
                var (scaled, shifted, mixed) = Triple(x, k);
                return (long)scaled + shifted + mixed;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Escaping(int a, int b)
            {
                var pair = Pair(a, b);
                var (sum, difference) = pair;
                return Sink(pair) + sum * 3 + difference;
            }

            public static int Run(int input)
            {
                var mixed = Mixed(input * 1.5, input + 2);
                return unchecked(Ints(input, 7) * 1000003 + (int)(mixed ^ (mixed >> 32)) * 31 + Escaping(input, -input * 3));
            }
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 210000723 },
        { 1, 242001086 },
        { 9, 498006966 },
        { 1000, -2103015261 },
        { -77, 2040698950 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DeconstructedTuplesNeedNoValueFrame(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"TupleDeconstruction_{target}", Source);

        var split = Compile(assets, assembly, target, null);
        var whole = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(StructScalarReplacement: ManagedStructScalarReplacement.Disabled));

        Assert.Equal(["Escaping"], Framed(assets, split, "Ints", "Mixed", "Escaping"));
        Assert.Equal(["Escaping", "Ints", "Mixed"], Framed(assets, whole, "Ints", "Mixed", "Escaping"));
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                split.ApplicationModule, assets.Directory, input, target, split.StaticDataEnd));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DeconstructedTuplesMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("TupleDeconstructionOz", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32, Optimization: FinalWasmOptimization.Oz));

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // The named EntryPoint methods that enter a value frame.
    private static string[] Framed(TestAssets assets, CompilationResult result, params string[] names)
    {
        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return [.. FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ', StringComparison.Ordinal)], Body: function))
            .Where(function => names.Any(name => function.Name == "$EntryPoint::" + name) && FrameEnter().IsMatch(function.Body))
            .Select(static function => function.Name["$EntryPoint::".Length..])
            .Order()];
    }

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$\S*value_frame_enter")]
    private static partial Regex FrameEnter();
}
