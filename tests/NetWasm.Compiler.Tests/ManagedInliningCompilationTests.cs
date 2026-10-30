using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedInliningCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, -1, 42)]
    [InlineData(WasmTarget.Wasm32, 0, 42)]
    [InlineData(WasmTarget.Wasm32, 2, 22)]
    [InlineData(WasmTarget.Wasm64, -1, 42)]
    [InlineData(WasmTarget.Wasm64, 0, 42)]
    [InlineData(WasmTarget.Wasm64, 2, 22)]
    public void TerminalContinuationRunsOnceOnlyAfterNormalCalleeReturn(
        WasmTarget target,
        int input,
        int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            $"TerminalContinuation_{target}_{input}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])
        {
            Optimization = FinalWasmOptimization.O3,
        });

        Assert.Equal(expected, result);
    }

    // The guard is inlined where AggressiveInlining is honored, and the try
    // that follows the call starts at its continuation.
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    public void InlinedReturnsStayOutsideAFollowingTry(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (-5, 2344), (0, -51556), (2, -666) })
        {
            Assert.Equal(expected, executor.Execute(new CompilationScenario(
                $"GuardBeforeTry_{target}_{preset}_{input}",
                GuardBeforeTrySource,
                "EntryPoint",
                Optimize: true,
                target,
                input,
                [])
            {
                Optimization = preset,
            }));
        }
    }

    private const string GuardBeforeTrySource = """
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var total = 0;
                for (var i = -2; i < 3; i++)
                {
                    try
                    {
                        Require(i == input ? null : i);
                        try
                        {
                            total = total * 7 + 12 / i;
                        }
                        catch (System.DivideByZeroException)
                        {
                            total += 100;
                        }
                    }
                    catch (System.ArgumentNullException)
                    {
                        total -= 1000;
                    }
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Require(object? value)
            {
                if (value == null)
                {
                    throw new System.ArgumentNullException(nameof(value));
                }
            }
        }
        """;

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static int s_phase;
            private static int s_suffixCalls;

            public static int Run(int input)
            {
                s_phase = 0;
                s_suffixCalls = 0;
                try
                {
                    return Caller(input);
                }
                catch (InvalidOperationException)
                {
                    return (input < 0 && s_phase == 0 && s_suffixCalls == 0) ||
                        (input == 0 && s_phase == 7 && s_suffixCalls == 1)
                        ? 42
                        : 0;
                }
            }

            private static int Caller(int input)
            {
                var state = 0;
                MutateOrThrow(ref state, input);
                s_phase = 7;
                s_suffixCalls++;
                MaybeThrowFromSuffix(input);
                return s_suffixCalls == 1 ? state + 1 : -1;
            }

            private static void MutateOrThrow(ref int state, int input)
            {
                if (input < 0)
                {
                    ThrowBeforeReturn();
                    return;
                }
                if (input == 0)
                {
                    state = 10;
                    return;
                }
                while (input > 1)
                {
                    state++;
                    input--;
                }
                state += 20;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ThrowBeforeReturn() =>
                throw new InvalidOperationException();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void MaybeThrowFromSuffix(int input)
            {
                if (input == 0)
                {
                    throw new InvalidOperationException();
                }
            }
        }
        """;
}
