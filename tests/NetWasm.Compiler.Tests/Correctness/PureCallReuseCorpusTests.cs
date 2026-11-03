using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Repeated pure Math and MathF calls: of a static field, an argument and a
// local, nested in each other and in arithmetic, and the near misses whose
// arguments change between the calls, through a call that writes the static
// field, a store to the local, a ref to it or a jump into the middle. Every
// result matches desktop to six decimals.
public sealed class PureCallReuseCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void PureCallReuseMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "PureCallReuseCorpus",
            "NetWasm.Correctness.PureCallReuse",
            Source,
            [.. Enumerable.Range(0, 3)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.PureCallReuse;

            public static class EntryPoint
            {
                private static double s_x;
                private static float s_f;

                public static int Run(int input)
                {
                    var hash = 17L;
                    for (var i = 1; i <= 40; i++)
                    {
                        s_x = i * 0.37 - 3;
                        s_f = i * 0.21f - 2;
                        var value = input switch
                        {
                            0 => Repeated(i * 0.11) + Nested(i * 0.5),
                            1 => Changed(i * 0.11) + Through(i * 0.7),
                            2 => Jumped(i, i * 0.3) + Single(),
                            _ => throw new ArgumentOutOfRangeException(nameof(input)),
                        };
                        // Six decimals: a reused result that should not be is far off, while
                        // the libraries' last bits may differ.
                        hash = unchecked(hash * 31 + (long)Math.Round(value * 1e6));
                    }
                    return unchecked((int)(hash ^ (hash >> 32)));
                }

                // As Benchstone's BenchMk2 computes its residue.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double Repeated(double a)
                {
                    var e = Math.Abs(Math.Log(Math.Exp(s_x)) / s_x) - Math.Sqrt(Math.Sin(s_x) * Math.Sin(s_x) + Math.Cos(s_x) * Math.Cos(s_x));
                    var b = Math.Sin(a / 3) * Math.Sin(a / 3) + Math.Atan2(a, s_x) - Math.Atan2(a, s_x) * 2;
                    return e + b + Math.Sin(a) * Math.Sin(a) * Math.Sin(a);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double Nested(double a) =>
                    Math.Sqrt(Math.Abs(Math.Sin(a))) - Math.Sqrt(Math.Abs(Math.Sin(a))) * 0.5 + Math.Sin(a);

                // Each repeat reads what changed in between.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double Changed(double a)
                {
                    var first = Math.Sin(s_x) * Bump() * Math.Sin(s_x);
                    var x = a;
                    var second = Math.Cos(x);
                    x += 1;
                    second -= Math.Cos(x);
                    a = a * 2;
                    return first + second + Math.Exp(a / 9) - Math.Exp(a / 9);
                }

                private static double Bump() => s_x += 0.5;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double Through(double a)
                {
                    var before = Math.Sin(a);
                    Twice(ref a);
                    return before - Math.Sin(a);
                }

                private static void Twice(ref double value) => value *= 2;

                // A loop's repeat after the jump into its condition.
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double Jumped(int count, double a)
                {
                    var sum = 0.0;
                    for (var k = 0; k < count % 4; k++)
                    {
                        sum += Math.Sin(a);
                        a += 0.25;
                    }
                    return sum + Math.Sin(a);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static double Single() =>
                    MathF.Sqrt(MathF.Abs(s_f)) * MathF.Sqrt(MathF.Abs(s_f)) + MathF.Cos(s_f) * MathF.Cos(s_f);

                public static int Trace() => 0;
            }
            """;
}
