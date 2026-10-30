namespace NetWasm.Compiler.Tests.Correctness;

// WebAssembly has no scalar FMA, so FusedMultiplyAdd is emulated: it must
// round the exact a * b + c once, as desktop's hardware instruction does,
// including where a product nearly cancels the addend, where rounding to
// double and then to float would round twice, and at the edges of the range.
[Collection(CorrectnessTestGroup.Name)]
public sealed class FusedMultiplyAddCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FusedMultiplyAddRoundsOnceAsDesktopHardwareDoes() =>
        runner.Run(new(
            "FusedMultiplyAddCorpus",
            "NetWasm.Correctness.FusedMultiplyAdd",
            """
            using System;

            namespace NetWasm.Correctness.FusedMultiplyAdd;

            public static class EntryPoint
            {
                private static ulong s_state;

                public static int Run(int input)
                {
                    s_state = 0x9E3779B97F4A7C15UL * (ulong)(input + 1);
                    var hash = 17;
                    for (var index = 0; index < 4000; index++)
                    {
                        hash = unchecked(hash * 31 + input switch
                        {
                            0 => Double(RandomDouble(60), RandomDouble(60), RandomDouble(120)),
                            1 => Cancelling(),
                            2 => Double(RandomDouble(600), RandomDouble(600), RandomDouble(1100)),
                            3 => Single(RandomSingle(20), RandomSingle(20), RandomSingle(40)),
                            4 => SingleMidpoint(),
                            _ => Edges(index),
                        });
                    }
                    return hash;
                }

                private static int Double(double a, double b, double c) =>
                    BitConverter.DoubleToInt64Bits(Math.FusedMultiplyAdd(a, b, c)).GetHashCode();

                private static int Single(float a, float b, float c) =>
                    BitConverter.SingleToInt32Bits(MathF.FusedMultiplyAdd(a, b, c));

                // The addend nearly cancels the product, so the low product bits decide.
                private static int Cancelling()
                {
                    var a = RandomDouble(10);
                    var b = RandomDouble(10);
                    var c = -(a * b) * (1 + ((long)(Next() % 7) - 3) * Pow2(-52));
                    return Double(a, b, c) * 3 + Double(a, b, -(a * b));
                }

                // Products whose exact sum with the addend lies just beside a float
                // rounding midpoint, where rounding to double first would land on it.
                private static int SingleMidpoint()
                {
                    var a = 1 + (int)(Next() % 4096) / 4096f;
                    var b = 1 + (int)(Next() % 4096) / 4096f;
                    var product = (double)a * b;
                    var below = (float)product;
                    var c = (float)((Next() & 1) == 0 ? Pow2(-25) : -Pow2(-25)) * (float)(1 + (Next() % 3));
                    // (1 + k 2^-12)^2 for odd k ends on a float rounding midpoint; a
                    // tiny addend moves the exact result off it by less than double
                    // keeps, so rounding to double first would tie instead.
                    var k = 1 + 2 * (int)(Next() % 50);
                    var square = 1 + k / 4096f;
                    var nudge = (float)((Next() & 1) == 0 ? Pow2(-60) : -Pow2(-60));
                    return Single(a, b, c) * 7 + Single(a, b, (float)(below - product)) * 3 + Single(square, square, nudge);
                }

                private static int Edges(int index)
                {
                    double[] doubles = [0.0, -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
                        double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1, -1, Pow2(-1022), Pow2(-1000),
                        Pow2(1000), 3, BitConverter.Int64BitsToDouble(0x3FEFFFFFFFFFFFFF)];
                    float[] singles = [0f, -0f, float.Epsilon, float.MaxValue, float.PositiveInfinity, float.NaN, 1, -1,
                        BitConverter.Int32BitsToSingle(0x00800000), 3, BitConverter.Int32BitsToSingle(0x3F7FFFFF)];
                    var a = doubles[index % doubles.Length];
                    var b = doubles[index / doubles.Length % doubles.Length];
                    var c = doubles[index / (doubles.Length * doubles.Length) % doubles.Length];
                    var x = singles[index % singles.Length];
                    var y = singles[index / singles.Length % singles.Length];
                    var z = singles[index / (singles.Length * singles.Length) % singles.Length];
                    var signaling = BitConverter.Int64BitsToDouble(0x7FF0000000000001);
                    return Double(a, b, c) * 5 + Single(x, y, z) + Double(1.5, b, signaling) * 11;
                }

                // Built from bits, so no other library function shapes the inputs.
                private static double RandomDouble(int exponentRange)
                {
                    // Finite: exponents past the range clamp to subnormal or the largest.
                    var exponent = (long)(Next() % (ulong)(2 * exponentRange + 1)) - exponentRange;
                    var field = (ulong)Math.Clamp(exponent + 1023, 0, 2046);
                    var bits = (Next() & 0x000FFFFFFFFFFFFFUL) | (field << 52) | (Next() & 0x8000000000000000UL);
                    return BitConverter.UInt64BitsToDouble(bits);
                }

                private static double Pow2(int exponent) => BitConverter.Int64BitsToDouble((long)(exponent + 1023) << 52);

                private static float RandomSingle(int exponentRange) => (float)RandomDouble(exponentRange);

                private static ulong Next()
                {
                    s_state ^= s_state << 13;
                    s_state ^= s_state >> 7;
                    s_state ^= s_state << 17;
                    return s_state;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
