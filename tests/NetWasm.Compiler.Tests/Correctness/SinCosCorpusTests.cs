namespace NetWasm.Compiler.Tests.Correctness;

// SinCos shares one argument reduction between both results, so each must stay
// what Sin and Cos return in every quadrant, at huge arguments and at the
// special values. Desktop's SinCos differs from its Sin and Cos by up to one
// ULP, so the budget is one ULP rather than equality.
[Collection(CorrectnessTestGroup.Name)]
public sealed class SinCosCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SinCosMatchesSinAndCosInEveryQuadrant() =>
        runner.Run(new(
            "SinCosCorpus",
            "NetWasm.Correctness.SinCos",
            """
            using System;

            namespace NetWasm.Correctness.SinCos;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    double[] specials = [0.0, -0.0, double.Epsilon, -double.Epsilon, 1e-300, Math.PI / 4,
                        -Math.PI / 4, Math.PI / 2, Math.PI, 3 * Math.PI / 2, 2 * Math.PI, 1e22, -1e22,
                        double.MaxValue, double.MinValue];
                    if (input == 6)
                    {
                        foreach (var special in specials)
                        {
                            if (!Close(special)) return -1;
                        }
                        var (nanSin, nanCos) = Math.SinCos(double.NaN);
                        var (infSin, infCos) = Math.SinCos(double.PositiveInfinity);
                        var (floatSin, floatCos) = MathF.SinCos(0.5f);
                        return double.IsNaN(nanSin) && double.IsNaN(nanCos) && double.IsNaN(infSin) &&
                            double.IsNaN(infCos) && Math.Abs(floatSin - 0.47942555f) < 1e-6f &&
                            Math.Abs(floatCos - 0.87758255f) < 1e-6f ? 42 : -2;
                    }
                    double[] ranges = [0.7, 4, 100, 1e6, 1e12, 1e300];
                    var state = 0x9E3779B97F4A7C15UL * (ulong)(input + 1);
                    for (var index = 0; index < 2000; index++)
                    {
                        state ^= state << 13;
                        state ^= state >> 7;
                        state ^= state << 17;
                        var x = ((state >> 11) * (1.0 / (1UL << 53)) * 2 - 1) * ranges[input];
                        if (!Close(x)) return -3 - index;
                    }
                    return 42;
                }

                private static bool Close(double x)
                {
                    var (sin, cos) = Math.SinCos(x);
                    return Ulps(sin, Math.Sin(x)) <= 1 && Ulps(cos, Math.Cos(x)) <= 1;
                }

                private static long Ulps(double left, double right)
                {
                    long a = BitConverter.DoubleToInt64Bits(left), b = BitConverter.DoubleToInt64Bits(right);
                    if (a < 0) a = long.MinValue - a;
                    if (b < 0) b = long.MinValue - b;
                    return Math.Abs(a - b);
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
