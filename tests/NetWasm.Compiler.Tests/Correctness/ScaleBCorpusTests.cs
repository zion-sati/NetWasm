using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Math.ScaleB and MathF.ScaleB, managed on every platform, over normal,
// subnormal, zero, infinite and NaN values and exponents past both ends of
// the range, where the scaling runs in two or three steps and rounds into the
// subnormals once. Every result matches desktop bit for bit.
public sealed class ScaleBCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void ScaleBMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ScaleBCorpus",
            "NetWasm.Correctness.ScaleB",
            Source,
            [.. Enumerable.Range(0, 2)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;

            namespace NetWasm.Correctness.ScaleB;

            public static class EntryPoint
            {
                private static readonly double[] Doubles =
                [
                    0, -0.0, 1, -1, 1.5, 3.0000000000000004, 0.1, -123456.789, double.Epsilon, -double.Epsilon,
                    2.2250738585072014E-308, 4.9406564584124654E-322, double.MaxValue, double.MinValue,
                    double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1.0000000000000002, 0.9999999999999999,
                ];

                private static readonly float[] Singles =
                [
                    0f, -0f, 1f, -1f, 1.5f, 3.0000002f, 0.1f, -123456.79f, float.Epsilon, -float.Epsilon,
                    1.1754944E-38f, 2.8E-44f, float.MaxValue, float.MinValue, float.PositiveInfinity,
                    float.NegativeInfinity, float.NaN, 1.0000001f, 0.99999994f,
                ];

                public static int Run(int input)
                {
                    var hash = 17L;
                    for (var n = -2300; n <= 2300; n += input == 0 ? 7 : 3)
                    {
                        if (input == 0)
                        {
                            foreach (var value in Doubles)
                            {
                                var result = Math.ScaleB(value, n);
                                hash = unchecked(hash * 31 + (double.IsNaN(result) ? -1 : BitConverter.DoubleToInt64Bits(result)));
                            }
                        }
                        else
                        {
                            foreach (var value in Singles)
                            {
                                var result = MathF.ScaleB(value, n);
                                hash = unchecked(hash * 31 + (float.IsNaN(result) ? -1 : BitConverter.SingleToInt32Bits(result)));
                            }
                        }
                    }
                    foreach (var n in new[] { int.MinValue, int.MinValue + 1, -1075, -1074, -150, -149, 0, 128, 1024, int.MaxValue })
                    {
                        hash = unchecked(hash * 31 + BitConverter.DoubleToInt64Bits(Math.ScaleB(1.25, n)));
                        hash = unchecked(hash * 31 + BitConverter.SingleToInt32Bits(MathF.ScaleB(1.25f, n)));
                    }
                    return unchecked((int)(hash ^ (hash >> 32)));
                }

                public static int Trace() => 0;
            }
            """;
}
