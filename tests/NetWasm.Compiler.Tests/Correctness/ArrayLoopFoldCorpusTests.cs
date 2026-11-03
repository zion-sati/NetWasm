using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// foreach loops over struct arrays, which the array unroller runs two or four
// elements a group, fold each element of a group into its own accumulator: folds of
// every operation, 32- and 64-bit, a step that does not reassociate, a body
// that divides inside a try whose handler reads the sum, one whose caller
// catches the exception, and one in a method with a protected region, all
// match desktop for every length from 0.
public sealed class ArrayLoopFoldCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.ArrayFolds;

        public struct S8 { public int A, B; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var size = 0; size <= 21; size++)
                {
                    var data = Data(size, input);
                    hash = unchecked(hash * 31 + input switch
                    {
                        0 => Folds(data),
                        1 => Unsplit(data),
                        2 => CaughtHere(data),
                        3 => CaughtByCaller(data),
                        4 => Guard(() => RegionElsewhere(data)),
                        _ => throw new ArgumentOutOfRangeException(nameof(input)),
                    });
                }
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Folds(S8[] data)
            {
                int sum = 0, all = -1, mix = 0;
                long wide = 0;
                foreach (var s in data)
                {
                    sum += s.A;
                    all &= s.B | 0x10;
                    mix ^= s.A << 1;
                    wide += s.B * 3L;
                }
                return unchecked(((sum * 31 + all) * 31 + mix) * 31 + (int)(wide ^ (wide >> 32)));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Unsplit(S8[] data)
            {
                var scaled = 1;
                foreach (var s in data) scaled = scaled * 3 + s.A;
                return scaled;
            }

            // The handler reads the sum the loop had reached.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CaughtHere(S8[] data)
            {
                var sum = 0;
                try
                {
                    foreach (var s in data) sum += 1000 / s.B;
                }
                catch (DivideByZeroException)
                {
                    return -sum;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Divides(S8[] data)
            {
                var sum = 0;
                foreach (var s in data) sum += 1000 / s.B;
                return sum;
            }

            private static int CaughtByCaller(S8[] data)
            {
                try
                {
                    return Divides(data);
                }
                catch (DivideByZeroException)
                {
                    return -1;
                }
            }

            // A protected region in the method, and a body that can throw: the
            // fold stays whole.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RegionElsewhere(S8[] data)
            {
                var sum = 0;
                try { sum = data.Length; }
                finally { sum++; }
                foreach (var s in data) sum += 1000 / s.B;
                return sum;
            }

            private static int Guard(Func<int> run)
            {
                try { return run(); }
                catch (DivideByZeroException) { return -2; }
            }

            private static S8[] Data(int size, int seed)
            {
                var data = new S8[size];
                for (var i = 0; i < size; i++)
                {
                    data[i] = new S8 { A = i * 7919 - 1000 + seed, B = (i * 13 + seed) % 11 == 9 ? 0 : i * 31 - 77 };
                }
                return data;
            }

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void ArrayFoldsMatchDesktop() =>
        runner.Run(new(
            "ArrayLoopFoldCorpus",
            "NetWasm.Correctness.ArrayFolds",
            Source,
            [0, 1, 2, 3, 4])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    // Folds split into an accumulator per extra element of a group; scaled
    // does not reassociate.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SplitFoldsAddTheirAccumulators(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayFolds" + target, Source);
        CompilationResult Compile(ManagedLoopUnrolling unrolling) =>
            NetWasmCompiler.Compile(new CompilerOptions(
                assembly, [assets.CoreLib], "NetWasm.Correctness.ArrayFolds.EntryPoint", "Run", [],
                Target: target, Optimization: FinalWasmOptimization.O3,
                OptimizationOverrides: new WasmOptimizationOverrides(LoopUnrolling: unrolling)));
        var unrolled = Compile(ManagedLoopUnrolling.Enabled);
        var rolled = Compile(ManagedLoopUnrolling.Disabled);
        int Added(string name)
        {
            int Locals(CompilationResult result) => result.Program.Methods.Values
                .Single(method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.ArrayFolds.EntryPoint" &&
                    method.Method.Definition.Name == name).Body.Locals.Length;
            return Locals(unrolled) - Locals(rolled);
        }

        // Each unrolled loop adds its length local, and each split fold an
        // accumulator per extra element of a group: Folds' four statements
        // unroll twice within the budget, Divides' one four times. The planner
        // leaves loops in methods with protected regions rolled; the unroller
        // would keep their throwing folds whole.
        Assert.Equal(
            [("Folds", 1 + 4), ("Unsplit", 1), ("CaughtHere", 0), ("Divides", 1 + 3), ("RegionElsewhere", 0)],
            ((string[])["Folds", "Unsplit", "CaughtHere", "Divides", "RegionElsewhere"]).Select(name => (name, Added(name))));
    }
}
