using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// MathF's Cbrt, Log10 and Acosh, which compute in single precision as the
// C library does, over a sweep through every branch: subnormals, the scaled
// and reduced ranges, and Acosh's three formulas. Each result lies within two
// units in the last place of the double function's rounded result, as on
// desktop, and the special values (zeros, infinities, negatives and NaN) are
// exact.
public sealed class SingleRootLogCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void SingleRootsAndLogsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "SingleRootLogCorpus",
            "NetWasm.Correctness.SingleRootLog",
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

            namespace NetWasm.Correctness.SingleRootLog;

            public static class EntryPoint
            {
                private static readonly float[] Specials =
                [
                    0f, -0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 1f, -1f, 2f, -2f,
                    float.Epsilon, -float.Epsilon, float.MaxValue, -float.MaxValue, 4096f, -0.5f, 0.999999f,
                ];

                public static int Run(int input) => input switch
                {
                    0 => Check(MathF.Cbrt, Math.Cbrt, 0x00000001, 0x7f7fffff, signed: true),
                    1 => Check(MathF.Log10, Math.Log10, 0x00000001, 0x7f7fffff, signed: false),
                    // [1, 2), [2, 4096) and [4096, MaxValue].
                    2 => Check(MathF.Acosh, Math.Acosh, 0x3f800000, 0x40000000, signed: false) * 31 +
                        Check(MathF.Acosh, Math.Acosh, 0x40000000, 0x45800000, signed: false) * 7 +
                        Check(MathF.Acosh, Math.Acosh, 0x45800000, 0x7f7fffff, signed: false),
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };

                // The count of results more than two units in the last place from the
                // double function's, then a hash of the special values' NaN,
                // infinite and zero results.
                private static int Check(Func<float, float> single, Func<double, double> reference, uint first, uint last, bool signed)
                {
                    var far = 0;
                    var step = (last - first) / 4093;
                    for (var bits = first; bits <= last && bits >= first; bits += step)
                    {
                        foreach (var sign in signed ? new[] { 1f, -1f } : new[] { 1f })
                        {
                            if (Far(single, reference, sign * BitConverter.UInt32BitsToSingle(bits))) far++;
                        }
                    }
                    var hash = 17;
                    foreach (var value in Specials)
                    {
                        var result = single(value);
                        if (float.IsFinite(result) && result != 0)
                        {
                            if (Far(single, reference, value)) far++;
                            continue;
                        }
                        hash = unchecked(hash * 31 + (float.IsNaN(result) ? -1 : BitConverter.SingleToInt32Bits(result)));
                    }
                    return unchecked(far * 1000003 + hash);
                }

                private static bool Far(Func<float, float> single, Func<double, double> reference, float value)
                {
                    var actual = single(value);
                    var expected = (float)reference(value);
                    return float.IsNaN(actual) != float.IsNaN(expected) || !float.IsNaN(actual) && Distance(actual, expected) > 2;
                }

                private static uint Distance(float left, float right)
                {
                    var a = BitConverter.SingleToInt32Bits(left);
                    var b = BitConverter.SingleToInt32Bits(right);
                    if ((a < 0) != (b < 0)) return left == right ? 0 : uint.MaxValue;
                    return (uint)Math.Abs((long)a - b);
                }

                public static int Trace() => 0;
            }
            """;
}
