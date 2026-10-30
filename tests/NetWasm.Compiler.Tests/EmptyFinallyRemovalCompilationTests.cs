using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// foreach over struct enumerators and using over structs whose Dispose does
// nothing leave an empty finally once Dispose is inlined; the region goes. A
// throw inside such a loop still reaches the caller's catch, a Dispose with an
// effect keeps its finally, and a catch around an empty finally still runs.
public sealed class EmptyFinallyRemovalCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;

        public static class EntryPoint
        {
            // Disposing it does nothing, so the finally around its use is empty.
            private struct Quiet : IDisposable
            {
                public int Value;
                public void Dispose() { }
            }

            // Disposing it is observable, so its finally stays.
            private struct Counted : IDisposable
            {
                public static int Disposed;
                public void Dispose() => Disposed++;
            }

            private static int SumList(List<int> values)
            {
                var sum = 0;
                foreach (var value in values)
                {
                    sum = sum * 3 + value;
                }
                return sum;
            }

            // A throw inside the loop reaches the caller's catch with nothing to run here.
            private static int SumUntilNegative(List<int> values)
            {
                var sum = 0;
                foreach (var value in values)
                {
                    if (value < 0) throw new InvalidOperationException("negative");
                    sum += value;
                }
                return sum;
            }

            private static int SumDictionary(Dictionary<int, string> values)
            {
                var sum = 0;
                foreach (var pair in values)
                {
                    sum += pair.Key * pair.Value.Length;
                }
                return sum;
            }

            private static int CountSet(HashSet<string> values)
            {
                var count = 0;
                foreach (var value in values)
                {
                    count += value[0];
                }
                return count;
            }

            private static int UseQuiet(int input)
            {
                using var quiet = new Quiet { Value = input };
                if (input > 4) return quiet.Value * 2;
                return quiet.Value - 1;
            }

            private static int UseCounted(int input)
            {
                using (new Counted())
                {
                    if (input > 4) return input;
                }
                return -input;
            }

            // The empty finally sits inside a try whose catch still has to run, and a
            // return from the catch leaves the catch as well.
            private static int CatchAroundQuiet(List<int> values)
            {
                try
                {
                    using var quiet = new Quiet();
                    return SumUntilNegative(values) + quiet.Value;
                }
                catch (InvalidOperationException exception)
                {
                    return -exception.Message.Length;
                }
            }

            public static int Run(int input)
            {
                var list = new List<int>();
                for (var i = 0; i < input; i++) list.Add(i * 7 - input);
                var dictionary = new Dictionary<int, string>();
                for (var i = 0; i < input; i++) dictionary[i] = new string('x', i % 4 + 1);
                var set = new HashSet<string>();
                for (var i = 0; i < input; i++) set.Add(((char)('a' + i % 26)).ToString());
                Counted.Disposed = 0;
                var counted = UseCounted(input) + UseCounted(input - 3);
                return SumList(list) % 100_000 + SumDictionary(dictionary) * 3 + CountSet(set) * 5 + UseQuiet(input) * 7 +
                    counted * 11 + Counted.Disposed * 13 + CatchAroundQuiet(list) * 17;
            }
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 52 },
        { 1, 385 },
        { 2, 866 },
        { 3, 1361 },
        { 5, 2341 },
        { 8, -10383 },
        { 13, -65479 },
        { 40, -73720 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3EmitsLessCodeThanWithEmptyFinallys(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"EmptyFinallyRemoval_{target}", Source);

        var removed = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var kept = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(EmptyFinallyRemoval: ManagedEmptyFinallyRemoval.Disabled));

        Assert.True(removed.ApplicationModule.Length < kept.ApplicationModule.Length,
            $"removed {removed.ApplicationModule.Length} bytes, kept {kept.ApplicationModule.Length}");
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                removed.ApplicationModule, assets.Directory, input, target, removed.StaticDataEnd));
        }
        Assert.Equal(385, ExecuteWithStandardWasiNode(
            kept.ApplicationModule, assets.Directory, 1, target, kept.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RemovedFinallysMatchDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("EmptyFinallyRemovalOz", Source);
        var result = Compile(assets, assembly, WasmTarget.Wasm32, FinalWasmOptimization.Oz, null);

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    private static CompilationResult Compile(
        TestAssets assets,
        string assembly,
        WasmTarget target,
        FinalWasmOptimization optimization,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: optimization, OptimizationOverrides: overrides));
}
