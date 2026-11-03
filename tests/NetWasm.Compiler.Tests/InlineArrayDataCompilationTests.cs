using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A one-dimensional array's elements sit at an inline offset every runtime
// keeps them at, so with inline array data its element addresses add that
// offset instead of loading the data pointer, which the array still keeps.
// Element access, loops the pass turns into fills and copies, Array.Copy, a
// span over an array, a multicast delegate's invocation list, Enum's arrays
// and a rectangular array, which keeps its elements after its shape, give
// desktop .NET's results either way.
public sealed class InlineArrayDataCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private enum Color { Red = 1, Green = 2, Blue = 4 }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sum(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < values.Length; i++) sum += values[i];
                return sum;
            }

            public static int Run(int input)
            {
                var ints = new int[input + 3];
                for (var i = 0; i < ints.Length; i++) ints[i] = i * 7 + input;
                var longs = new long[4];
                for (var i = 0; i < longs.Length; i++) longs[i] = (long)input << 33;
                var copy = new int[ints.Length];
                Array.Copy(ints, copy, ints.Length);
                var span = ints.AsSpan(1);
                span[0] += 5;
                var objects = new object[] { "a", input, 3.5 };
                var text = string.Join(",", objects);
                var grid = new int[2, 3];
                grid[1, 2] = input;
                var hits = 0;
                Action? multicast = null;
                multicast += () => hits++;
                multicast += () => hits += 10;
                multicast();
                var names = Enum.GetNames<Color>();
                var values = Enum.GetValues<Color>();
                return Sum(ints) + Sum(copy) * 3 + (int)(longs[3] >> 33) + span.Length * 5 + text.Length * 17 +
                    grid[1, 2] * 11 + hits * 13 + names[2].Length * 19 + (int)values[2] * 23;
            }
        }
        """;

    // As desktop .NET computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 529), (1, 646), (5, 1474), (40, 33306)];

    [Theory]
    [InlineData(WasmTarget.Wasm32, ManagedInlineArrayData.Auto)]
    [InlineData(WasmTarget.Wasm32, ManagedInlineArrayData.Disabled)]
    [InlineData(WasmTarget.Wasm64, ManagedInlineArrayData.Auto)]
    [InlineData(WasmTarget.Wasm64, ManagedInlineArrayData.Disabled)]
    public void ArraysGiveDesktopResultsWithAndWithoutInlineData(WasmTarget target, ManagedInlineArrayData inlineData)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"InlineArrayData_{target}_{inlineData}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new WasmOptimizationOverrides(InlineArrayData: inlineData)));

        // Only the data pointer is read at its offset: an address-sized load.
        var pointer = target == WasmTarget.Wasm64 ? "i64.load offset=16" : "i32.load offset=8";
        var sum = Function(assets, result, "EntryPoint::Sum");
        Assert.Equal(inlineData == ManagedInlineArrayData.Disabled, sum.Contains(pointer, StringComparison.Ordinal));
        foreach (var (input, expected) in Cases)
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static string Function(TestAssets assets, CompilationResult result, string name)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var function = Regex.Match(printed.StandardOutput,
            $@"\(func \${Regex.Escape(name)}\b.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.True(function.Success, $"{name} was not emitted.");
        return function.Value;
    }
}
