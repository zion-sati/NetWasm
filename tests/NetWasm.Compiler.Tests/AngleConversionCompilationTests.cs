using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// DegreesToRadians and RadiansToDegrees are correctly rounded, as .NET 11 RC3
// computes them: double carries pi / 180 as three limbs, float, Half and
// BFloat16 multiply in double, and the Vector128 and Vector2 forms agree with
// the scalar ones. The expected checksums are desktop .NET 11 RC3's
// (11.0.0-rc.3.26522.148); value * (pi / 180) rounds 127 of the 800 scalar
// conversions in the generated range differently.
public sealed class AngleConversionCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, -1064532233)]
    [InlineData(WasmTarget.Wasm32, 1, 435000497)]
    [InlineData(WasmTarget.Wasm32, 2, -1846869247)]
    [InlineData(WasmTarget.Wasm32, 3, -543417846)]
    [InlineData(WasmTarget.Wasm32, 4, 805298795)]
    [InlineData(WasmTarget.Wasm32, 5, 1790320798)]
    [InlineData(WasmTarget.Wasm32, 6, -1846869247)]
    [InlineData(WasmTarget.Wasm64, 0, -1064532233)]
    [InlineData(WasmTarget.Wasm64, 5, 1790320798)]
    public void MatchesDesktopBitForBit(WasmTarget target, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"AngleConversion_{target}_{input}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])));
    }

    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                double[] values =
                [
                    double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0, double.Epsilon, -double.Epsilon,
                    double.MaxValue, double.MinValue, 1e-300, Math.ScaleB(1, -960), Math.ScaleB(1, -961), Math.ScaleB(1, -971),
                    Math.ScaleB(-1.5, -972), 3.137566414384587E+306, 3.1375664143845866E+306, 1e306, 180.0, 90.0, 45.0,
                    Math.PI, Math.PI / 2, 1.0, -1.0, 360.0, 1e10, 1e-10, 12345.678, 1e-40, 3e38, 70000.0, 65504.0, 1e-5,
                ];
                var checksum = 0L;
                foreach (var value in values)
                {
                    checksum = checksum * 31 + Group(value, input);
                }
                // Spread across the exponent range, where most products round differently from value * (pi / 180).
                var state = 0x9E3779B97F4A7C15UL;
                for (var index = 0; index < 400; index++)
                {
                    state = state * 6364136223846793005UL + 1442695040888963407UL;
                    var mantissa = 1.0 + (state >> 11) / 9007199254740992.0;
                    var value = Math.ScaleB(index % 2 == 0 ? mantissa : -mantissa, (int)(state >> 58) - 32);
                    checksum = checksum * 31 + Group(value, input);
                }
                return (int)(checksum ^ checksum >> 32);
            }

            private static long Group(double value, int input) => input switch
            {
                0 => Bits(double.DegreesToRadians(value)),
                1 => Bits(double.RadiansToDegrees(value)),
                2 => Bits(float.DegreesToRadians((float)value)) + 3 * Bits(float.RadiansToDegrees((float)value)),
                3 => Bits((float)Half.DegreesToRadians((Half)value)) + 3 * Bits((float)Half.RadiansToDegrees((Half)value)),
                4 => Bits((float)BFloat16.DegreesToRadians((BFloat16)value)) + 3 * Bits((float)BFloat16.RadiansToDegrees((BFloat16)value)),
                5 => Bits(Vector128.DegreesToRadians(Vector128.Create(value, -value)).GetElement(1)) +
                    3 * Bits(Vector128.RadiansToDegrees(Vector128.Create(value * 3, value)).GetElement(1)),
                _ => Bits(Vector128.DegreesToRadians(Vector128.Create((float)value)).GetElement(2)) +
                    3 * Bits(Vector2.RadiansToDegrees(new Vector2((float)value, 1)).X),
            };

            // NaN payloads differ between platforms; every NaN counts the same.
            private static long Bits(double value) => double.IsNaN(value) ? 1 : BitConverter.DoubleToInt64Bits(value);

            private static long Bits(float value) => float.IsNaN(value) ? 1 : BitConverter.SingleToInt32Bits(value);
        }
        """;
}
