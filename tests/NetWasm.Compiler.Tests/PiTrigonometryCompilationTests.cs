using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// SinPi, CosPi and TanPi reduce to a quarter period before evaluating, as
// desktop .NET's do, rather than multiplying by pi first.
public sealed class PiTrigonometryCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, 635272687)]
    [InlineData(WasmTarget.Wasm32, 5, -1879549987)]
    [InlineData(WasmTarget.Wasm64, 0, 635272687)]
    public void MatchesDesktopBitForBit(WasmTarget target, int input, int expected)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        // As desktop .NET computes it.
        Assert.Equal(expected, executor.Execute(new CompilationScenario(
            $"PiTrigonometry_{target}_{input}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            input,
            [])));
    }

    private const string Source = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                double[] values =
                [
                    0.0, -0.0, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, -0.5, -1.25, 0.1, 1e-10, 3e-5, 0.4999999, 0.5000001,
                    1e15 + 0.5, 4503599627370497.0, 18014398509481986.0, 123.456 + input, double.PositiveInfinity, double.NaN,
                ];
                var checksum = 0L;
                foreach (var value in values)
                {
                    var (sin, cos) = double.SinCosPi(value);
                    checksum = checksum * 31 + Bits(double.SinPi(value)) + 3 * Bits(double.CosPi(value)) + 5 * Bits(double.TanPi(value));
                    checksum = checksum * 31 + Bits(sin) + 7 * Bits(cos);
                    var single = (float)value;
                    var (sinSingle, cosSingle) = float.SinCosPi(single);
                    checksum = checksum * 31 + Bits(float.SinPi(single)) + 3 * Bits(float.CosPi(single)) + 5 * Bits(float.TanPi(single));
                    checksum = checksum * 31 + Bits(sinSingle) + 7 * Bits(cosSingle);
                }

                // The TanPi benchmarks sum across the poles at half-integers.
                double sum = 0.0, x = -1.0;
                float sumSingle = 0f, xSingle = -1f;
                for (var iteration = 0; iteration < 5000; iteration++)
                {
                    x += 0.0004;
                    sum += double.TanPi(x);
                    xSingle += 0.0004f;
                    sumSingle += float.TanPi(xSingle);
                }
                checksum = checksum * 31 + Bits(sum) + 3 * Bits(sumSingle);
                return (int)(checksum ^ checksum >> 32);
            }

            // NaN payloads differ between platforms; every NaN counts the same.
            private static long Bits(double value) => double.IsNaN(value) ? 1 : BitConverter.DoubleToInt64Bits(value);

            private static long Bits(float value) => float.IsNaN(value) ? 1 : BitConverter.SingleToInt32Bits(value);
        }
        """;
}
