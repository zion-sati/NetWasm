using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An inlined struct operator spills its arguments by loading them all before
// storing any, so each copy into a parameter has other locals' loads and stores
// between its load and its store. Those copies go field by field, and a complex
// Mandelbrot loop keeps every value in locals. An argument whose local a later
// argument changes before the spill keeps the value it had when it was loaded.
public sealed partial class StructArgumentSpillCompilationTests
{
    private const string Source = """
        using System.Runtime.CompilerServices;

        public struct Complex
        {
            public Complex(double r, double i) { R = r; I = i; }
            public double R;
            public double I;
            public Complex Square() => new Complex(R * R - I * I, 2.0 * R * I);
            public double SqAbs() => R * R + I * I;
            public static Complex operator +(Complex a, Complex b) => new Complex(a.R + b.R, a.I + b.I);
        }

        public struct Pair
        {
            public Pair(int a, int b) { A = a; B = b; }
            public int A;
            public int B;
            public static Pair Sum(Pair left, Pair right) => new Pair(left.A * 3 + right.A, left.B - right.B * 7);
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double Mandelbrot(double step)
            {
                double result = 0;
                for (double y = -1.5; y < 1.5; y += step)
                {
                    for (double x = -2.0; x < 1.0; x += step)
                    {
                        var num = new Complex(x, y);
                        var accum = num;
                        int iterations;
                        for (iterations = 0; iterations < 200; iterations++)
                        {
                            accum = accum.Square();
                            accum += num;
                            if (accum.SqAbs() > 4.0) break;
                        }
                        result += iterations;
                    }
                }
                return result;
            }

            private static Pair Mutate(ref Pair pair)
            {
                pair.A += 100;
                pair.B -= 9;
                return new Pair(pair.B, pair.A);
            }

            // The first argument is the pair before Mutate changes it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Spilled(int seed)
            {
                var pair = new Pair(seed, seed * 5);
                var sum = Pair.Sum(pair, Mutate(ref pair));
                return sum.A * 31 + sum.B + pair.A;
            }

            public static int Run(int input) => (int)Mandelbrot(0.5 / (input % 7 + 1)) * 1000 + Spilled(input);
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 2222121 },
        { 1, 6245368 },
        { 3, 22733862 },
        { 6, 65854603 },
        { 40, 47322001 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SpilledArgumentsStayInLocals(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"StructArgumentSpill_{target}", Source);

        var split = Compile(assets, assembly, target, null);
        var whole = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(StructScalarReplacement: ManagedStructScalarReplacement.Disabled));

        Assert.False(Framed(assets, split, "Mandelbrot"));
        Assert.True(Framed(assets, whole, "Mandelbrot"));
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                split.ApplicationModule, assets.Directory, input, target, split.StaticDataEnd));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SpilledArgumentsMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("StructArgumentSpillOz", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32, Optimization: FinalWasmOptimization.Oz));

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Whether the EntryPoint method enters a value frame.
    private static bool Framed(TestAssets assets, CompilationResult result, string name)
    {
        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return FrameEnter().IsMatch(FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Single(function => function.StartsWith("$EntryPoint::" + name + " ", StringComparison.Ordinal)));
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
