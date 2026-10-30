using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Goto-heavy methods whose control flow is reducible: loops left from inside
// at more than one place, a goto out of two loops, values left on the stack
// across branches, and goto case. Structuring used to route them through a
// dispatcher; they are now labeled blocks, loops and branches.
public sealed class DispatcherEliminationCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            // Bisection with exits to two places from inside its loop.
            private static int Bisect(double a, double b, double tolerance, out double root)
            {
                double fa, error, middle, fm;
                var flag = 0;
                root = 0;
                fa = F(a);
                if (fa * F(b) < 0.0) goto start;
                flag = 2;
                goto done;
            start:
                error = Math.Abs(b - a);
            halve:
                error /= 2.0;
                if (error < tolerance) goto done;
                middle = (a + b) / 2.0;
                if (middle + error == middle) goto stuck;
                fm = F(middle);
                if (fa * fm < 0.0) goto right;
                a = middle;
                fa = fm;
                goto halve;
            right:
                b = middle;
                goto halve;
            stuck:
                flag = 1;
            done:
                root = (a + b) / 2.0;
                return flag;
            }

            private static double F(double x) => -1.0 - x * (1.0 - x * x);

            // Leaves both loops from the inner one.
            private static int Find(int limit, int target)
            {
                int i, j;
                for (i = 0; i < limit; i++)
                {
                    for (j = 0; j < 7; j++)
                    {
                        if (i * 7 + j == target) goto found;
                        if (j * i > 40) goto next;
                    }
                next:;
                }
                return -1;
            found:
                return i * 100 + j;
            }

            // Values left on the evaluation stack across the branches of a loop.
            private static int Mixed(int n)
            {
                var sum = 0;
                for (var i = 0; i < n; i++)
                {
                    sum += (i & 1) == 0 ? i * (n > 3 ? 2 : 3) : -i;
                    if (sum > 60) goto over;
                    if (sum < -60) break;
                }
                return sum;
            over:
                return sum * 10 + (n > 5 ? 1 : 2);
            }

            // A state machine written with goto case.
            private static int States(int input)
            {
                var state = input & 3;
                var steps = 0;
                var value = input;
                while (steps < 40)
                {
                    steps++;
                    switch (state)
                    {
                        case 0:
                            value += 3;
                            if (value % 5 == 0) goto case 2;
                            state = 1;
                            break;
                        case 1:
                            value *= 2;
                            if (value > 500) goto end;
                            goto case 3;
                        case 2:
                            value -= 7;
                            state = 3;
                            continue;
                        case 3:
                            value ^= steps;
                            state = value & 1;
                            break;
                    }
                }
            end:
                return value * 64 + steps;
            }

            public static int Run(int input)
            {
                var flag = Bisect(1.0, 2.0 + input, 1e-9, out var root);
                return flag * 1_000_000 + (int)(root * 1000) + Find(input + 2, input * 3) * 7 + Mixed(input) * 11 + States(input);
            }
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<int, int> Cases => new()
    {
        { 0, 33722 },
        { 1, 33743 },
        { 2, -22995725 },
        { 5, 42647 },
        { 7, 48017 },
        { 12, 41331 },
        { -3, -64250847 },
        { 1000, 136644 },
    };

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3EmitsLessCodeThanWithDispatchers(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"DispatcherElimination_{target}", Source);

        var labeled = Compile(assets, assembly, target, FinalWasmOptimization.O3, null);
        var dispatched = Compile(assets, assembly, target, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(DispatcherElimination: ManagedDispatcherElimination.Disabled));

        Assert.True(labeled.ApplicationModule.Length < dispatched.ApplicationModule.Length,
            $"labeled {labeled.ApplicationModule.Length} bytes, dispatched {dispatched.ApplicationModule.Length}");
        foreach (var (input, expected) in Cases.Select(row => ((int)row[0], (int)row[1])))
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                labeled.ApplicationModule, assets.Directory, input, target, labeled.StaticDataEnd));
        }
        Assert.Equal(33743, ExecuteWithStandardWasiNode(
            dispatched.ApplicationModule, assets.Directory, 1, target, dispatched.StaticDataEnd));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LabeledControlFlowMatchesDesktopAtOz(int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DispatcherEliminationOz", Source);
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
