using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// IRootFunctions' Hypot and RootN of float and double over every pair of edge
// values, as generic code reaches them. Hypot is managed arithmetic on every
// platform, so each result matches desktop bit for bit: +Infinity when either
// input is infinite, a NaN beside it included, and no overflow or underflow
// for large and tiny inputs. RootN's results keep their sign, NaN, infinities
// and signed zeros exactly and the pow it rounds through to nine digits.
public sealed class RootFunctionsCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void RootFunctionsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "RootFunctionsCorpus",
            "NetWasm.Correctness.RootFunctions",
            Source,
            [.. Enumerable.Range(0, 4)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.RootFunctions;

            public static class EntryPoint
            {
                private static readonly double[] Doubles =
                [
                    0, -0.0, 1, -3, 4, 3.0000000000000004, 1e-20, 1e300, -1e300, 1e-300, double.Epsilon,
                    double.MaxValue, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
                ];

                private static readonly float[] Floats =
                [
                    0, -0.0f, 1, -3, 4, 3.0000002f, 1e-20f, 3e38f, -3e38f, 1e-38f, float.Epsilon,
                    float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
                ];

                private static readonly double[] Radicands =
                [
                    0, -0.0, 1, -1, 4, -8, 16, -32, 2.5, double.Epsilon, double.PositiveInfinity,
                    double.NegativeInfinity, double.NaN,
                ];

                private static readonly int[] Degrees = [int.MinValue, -3, -2, -1, 0, 1, 2, 3, 4, 5];

                public static int Run(int input) => input switch
                {
                    0 => Hypot(Doubles),
                    1 => Hypot(Floats),
                    2 => RootN<double>(),
                    3 => RootN<float>(),
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };

                public static int Trace() => 0;

                private static int Hypot<T>(T[] values) where T : IRootFunctions<T>, IFloatingPointIeee754<T>
                {
                    var hash = 17;
                    foreach (var x in values)
                    {
                        foreach (var y in values)
                        {
                            hash = Add(hash, Bits(double.CreateTruncating(T.Hypot(x, y))));
                        }
                    }
                    return hash;
                }

                private static int RootN<T>() where T : IRootFunctions<T>, IFloatingPointIeee754<T>
                {
                    var hash = 17;
                    foreach (var radicand in Radicands)
                    {
                        foreach (var degree in Degrees)
                        {
                            var root = double.CreateTruncating(T.RootN(T.CreateTruncating(radicand), degree));
                            hash = Add(hash, Bits(double.IsFinite(root) && root != 0 ? Math.Round(root, 9) : root));
                        }
                    }
                    return hash;
                }

                // NaN hashes alike: Wasm does not keep the NaN sign desktop .NET produces.
                private static long Bits(double value) => double.IsNaN(value) ? 1 : BitConverter.DoubleToInt64Bits(value);

                private static int Add(int hash, long bits) => unchecked((hash * 31 + (int)bits) * 31 + (int)(bits >> 32));
            }
            """;
}
