using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class MathFExpLogCompilationTests
{
    // MathF.Exp, Log, Log2 and Pow are musl's float routines (Arm's), which Mono and desktop .NET on
    // Linux also run. Each row is IEEE bits that musl v1.2.5 returns (an Emscripten build as the
    // reference) for an input where rounding the double function's result gives a different float, so
    // the rows fail if the float functions go back to the double ones. Special values follow.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void FloatExpLogAndPowReturnMuslsResults(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"MathFExpLog_{optimize}_{target}", Source, "MathFExpLog.EntryPoint", optimize, target, 41, []));

        // A negative result identifies the first mismatching row: -100 - row for Exp, -200 - row for
        // Log, -300 - row for Log2, -400 - row for Pow and -500 - row for the special values.
        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;

        namespace MathFExpLog;

        public static class EntryPoint
        {
            private static readonly uint[] Exp =
            [
                0xbc8096d4, 0x3f7c0352, 0x40da89ab, 0x44672316, 0xc19f7b39, 0x311720ed, 0x3eceef92, 0x3fbfc076,
                0x42ac7119, 0x7da7c88b, 0xbeb8e516, 0x3f32679c, 0x3f7d952f, 0x402c55c8, 0xbbae4127, 0x3f7ea46b,
                0xbbefd5e5, 0x3f7e2215, 0x3f89dd80, 0x403be895, 0xbbd2b54f, 0x3f7e5bf0, 0x401a24d1, 0x4131e038,
            ];

            private static readonly uint[] Log =
            [
                0x41b2a214, 0x4046c6f2, 0x46038643, 0x41109bf5, 0x3b3ad6a5, 0xc0bb8606, 0x4a935fd8, 0x41763e31,
                0x3ead0f28, 0xbf8ad727, 0x3f71e358, 0xbd6840c1, 0x3f1c8942, 0xbefbd90f, 0x561bae49, 0x41fb1965,
                0x3f831d3c, 0x3cc4ec34, 0x3f818842, 0x3c42f6dc, 0x3f828453, 0x3c9f8491, 0x3f347ba6, 0xbeb2f705,
            ];

            private static readonly uint[] Log2 =
            [
                0x3f7fffff, 0xb3b8aa3b, 0x4c9c0960, 0x41d24932, 0x3f828021, 0x3ce4a6cb, 0x3f803680, 0x3b1d1f85,
                0x422c01ea, 0x40ada479, 0x3ffae0c8, 0x3f788922, 0x0fd0ef75, 0xc2be9610, 0x344e1aad, 0xc1b2808d,
                0x400c0df8, 0x3f909ec4, 0x3f7aae13, 0xbcf83180, 0x3dd66877, 0xc0505ec6, 0x3f7c903e, 0xbc9fba30,
            ];

            private static readonly uint[] Pow =
            [
                0x13d76fe2, 0x3afb8961, 0x3f63f2c7, 0x29c0c221, 0xbaf5caf3, 0x3f876e14,
                0x2850c9d0, 0xbde39adc, 0x420d8633, 0x221538b1, 0xbb6ce69b, 0x3f944f6c,
                0x08605568, 0xb98a344e, 0x3f829a6d, 0x304c97b8, 0xbdb1d2e0, 0x40c67d52,
                0x76abe18e, 0xb8ab895f, 0x3f7e66fc, 0x7bb2605b, 0x3c0da8cc, 0x4003c094,
                0x267b8449, 0x3bdc4d68, 0x3f4ac42b, 0x7148462a, 0xba163cb7, 0x3f76107c,
                0x395a6ab0, 0x3a26b890, 0x3f7e9fa6, 0x53ad2400, 0xb92416fd, 0x3f7ee130,
            ];

            public static int Run(int input)
            {
                for (var row = 0; row < Exp.Length / 2; row++)
                    if (Bits(MathF.Exp(Value(Exp[2 * row]))) != Exp[2 * row + 1]) return -100 - row;
                for (var row = 0; row < Log.Length / 2; row++)
                    if (Bits(MathF.Log(Value(Log[2 * row]))) != Log[2 * row + 1]) return -200 - row;
                for (var row = 0; row < Log2.Length / 2; row++)
                    if (Bits(MathF.Log2(Value(Log2[2 * row]))) != Log2[2 * row + 1]) return -300 - row;
                for (var row = 0; row < Pow.Length / 3; row++)
                    if (Bits(MathF.Pow(Value(Pow[3 * row]), Value(Pow[3 * row + 1]))) != Pow[3 * row + 2]) return -400 - row;
                bool[] specials =
                [
                    MathF.Exp(float.NegativeInfinity) == 0f, MathF.Exp(float.PositiveInfinity) == float.PositiveInfinity,
                    float.IsNaN(MathF.Exp(float.NaN)), MathF.Exp(89f) == float.PositiveInfinity, MathF.Exp(-104f) == 0f,
                    MathF.Exp(0f) == 1f, MathF.Log(1f) == 0f && !float.IsNegative(MathF.Log(1f)),
                    MathF.Log(0f) == float.NegativeInfinity, MathF.Log(-0f) == float.NegativeInfinity,
                    float.IsNaN(MathF.Log(-1f)), MathF.Log(float.PositiveInfinity) == float.PositiveInfinity,
                    float.IsNaN(MathF.Log(float.NegativeInfinity)), MathF.Log2(8f) == 3f,
                    MathF.Log2(float.Epsilon) == -149f, float.IsNaN(MathF.Log2(-2f)),
                    MathF.Pow(float.NaN, 0f) == 1f, MathF.Pow(1f, float.NaN) == 1f, float.IsNaN(MathF.Pow(-8f, 1f / 3f)),
                    MathF.Pow(-2f, 3f) == -8f, MathF.Pow(-0f, -1f) == float.NegativeInfinity, MathF.Pow(0f, -2f) == float.PositiveInfinity,
                    MathF.Pow(-1f, float.PositiveInfinity) == 1f, MathF.Pow(0.5f, float.PositiveInfinity) == 0f,
                    MathF.Pow(2f, float.NegativeInfinity) == 0f, MathF.Pow(10f, 39f) == float.PositiveInfinity,
                    MathF.Pow(2f, -150f) == 0f && !float.IsNegative(MathF.Pow(2f, -150f)), MathF.Pow(-2f, -151f) == 0f && float.IsNegative(MathF.Pow(-2f, -151f)),
                    float.Exp2(10f) == 1024f, float.Exp10(3f) == 1000f, float.Exp10(-1f) == 0.1f,
                ];
                for (var row = 0; row < specials.Length; row++)
                    if (!specials[row]) return -500 - row;
                return 42;
            }

            private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);

            private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        }
        """;
}
