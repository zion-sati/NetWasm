using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A counted loop over a span, `for (i = 0; i < span.Length; i++)` or a
// foreach, keeps its index below the split span's length, so the indexer's
// check of i, and of i + 1 under an i + 1 < Length guard, never throws: Sum,
// Fill, Pairs and Structs call no ThrowIndexOutOfRangeException, and Sum and
// Fill unroll as array loops do. An i - 1 index, an i <= Length guard and a
// loop that replaces its span keep their checks, and Past still throws.
public sealed partial class SpanLoopBoundsCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public struct Pair
        {
            public int A, B;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sum(ReadOnlySpan<int> span)
            {
                var sum = 0;
                foreach (var value in span) sum += value | 1;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fill(Span<int> span, int value)
            {
                for (var i = 0; i < span.Length; i++) span[i] = value + i;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Pairs(ReadOnlySpan<int> span)
            {
                var sum = 0;
                for (var i = 0; i + 1 < span.Length; i++) sum += span[i] * span[i + 1];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Structs(ReadOnlySpan<Pair> span)
            {
                var sum = 0;
                foreach (var pair in span) sum += pair.A * 3 + pair.B;
                return sum;
            }

            // Shapes the proof does not cover, which keep their checks.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Shifted(ReadOnlySpan<int> span)
            {
                var sum = 0;
                for (var i = 1; i < span.Length; i++) sum += span[i - 1] * i;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Past(ReadOnlySpan<int> span)
            {
                var sum = 0;
                for (var i = 0; i <= span.Length; i++) sum += span[i];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sliding(ReadOnlySpan<int> span)
            {
                var sum = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    sum = sum * 3 + span[i];
                    if ((span[i] & 3) == 0) span = span.Slice(1);
                }
                return sum;
            }

            public static int Run(int input)
            {
                var values = new int[input + input % 5];
                Fill(values, input - 7);
                var pairs = new Pair[input % 9];
                for (var i = 0; i < pairs.Length; i++) pairs[i] = new Pair { A = i, B = input - i };
                var result = Sum(values) * 3 + Pairs(values) * 5 + Structs(pairs) * 7 + Shifted(values) * 11 + Sliding(values) * 13;
                try
                {
                    result += Past(values);
                }
                catch (IndexOutOfRangeException)
                {
                    result += 17;
                }
                return result;
            }
        }
        """;

    // As .NET 11 computes them, each in a new process.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 17 },
        { 1, -221 },
        { 2, -796 },
        { 3, -1693 },
        { 5, -432 },
        { 13, 22622523 },
        { 40, 1298890454 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    public void SpanLoopsNeedNoIndexChecks(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"SpanLoopBounds_{target}_{optimization}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        string[] probes = ["Sum", "Fill", "Pairs", "Structs", "Shifted", "Past", "Sliding"];
        Assert.Equal(new Dictionary<string, bool>
        {
            ["Sum"] = false,
            ["Fill"] = false,
            ["Pairs"] = false,
            ["Structs"] = false,
            ["Shifted"] = true,
            ["Past"] = true,
            ["Sliding"] = true,
        }, Checked(assets, result, probes));
        foreach (var (input, expected) in Cases.Select(row => row.Data))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // Whether each EntryPoint method can throw IndexOutOfRangeException.
    private static Dictionary<string, bool> Checked(TestAssets assets, CompilationResult result, string[] names)
    {
        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ', StringComparison.Ordinal)], Body: function))
            .Where(function => names.Any(name => function.Name == "$EntryPoint::" + name))
            .ToDictionary(static function => function.Name["$EntryPoint::".Length..],
                static function => function.Body.Contains("ThrowIndexOutOfRangeException", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();
}
