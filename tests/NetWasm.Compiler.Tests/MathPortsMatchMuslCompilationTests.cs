// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Math and MathF call C# ports of musl's libm in place of the C library where the port runs faster
// (Math.NativeAot.cs, MathF.NativeAot.cs and Primitives.cs say which, and why), and each port must give musl's
// results bit for bit. These compile each port into a program, inlined where the compiler inlines it and with a
// constant argument folded where a caller passes one, and compare it with musl's own function, which the test host
// supplies as libc (oracle-libm.mjs). SinCos calls musl's sincos and must agree with its sin and cos. Inputs: special
// values, pseudo-random doubles across every binade and in everyday ranges, and a stride through every float. The
// ports were also checked once on far more (every float; 50 million doubles).
// Each case returns the number of inputs whose results differ; two NaNs agree whatever their payloads.
public sealed class MathPortsMatchMuslCompilationTests
{
    public static TheoryData<int, WasmTarget> Cases()
    {
        var cases = new TheoryData<int, WasmTarget>();
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            for (var function = 0; function < FunctionCount; function++) cases.Add(function, target);
        }
        return cases;
    }

    private const int FunctionCount = 15;

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachPortGivesMuslsResultsBitForBit(int function, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("MathPortsFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "MathPortsFixture.EntryPoint", "Run", [], Target: target));
        Assert.Equal(0, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, function, target,
            result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace MathPortsFixture;

        public static class EntryPoint
        {
            [DllImport("libc", EntryPoint = "asin")] private static extern double MuslAsin(double x);
            [DllImport("libc", EntryPoint = "acos")] private static extern double MuslAcos(double x);
            [DllImport("libc", EntryPoint = "pow")] private static extern double MuslPow(double x, double y);
            [DllImport("libc", EntryPoint = "exp2")] private static extern double MuslExp2(double x);
            [DllImport("libc", EntryPoint = "sin")] private static extern double MuslSin(double x);
            [DllImport("libc", EntryPoint = "cos")] private static extern double MuslCos(double x);
            [DllImport("libc", EntryPoint = "expf")] private static extern float MuslExpf(float x);
            [DllImport("libc", EntryPoint = "logf")] private static extern float MuslLogf(float x);
            [DllImport("libc", EntryPoint = "log2f")] private static extern float MuslLog2f(float x);
            [DllImport("libc", EntryPoint = "powf")] private static extern float MuslPowf(float x, float y);
            [DllImport("libc", EntryPoint = "acoshf")] private static extern float MuslAcoshf(float x);
            [DllImport("libc", EntryPoint = "cbrtf")] private static extern float MuslCbrtf(float x);
            [DllImport("libc", EntryPoint = "sinf")] private static extern float MuslSinf(float x);
            [DllImport("libc", EntryPoint = "cosf")] private static extern float MuslCosf(float x);

            public static int Run(int function) => function switch
            {
                0 => Doubles(x => Math.Asin(x), x => MuslAsin(x)),
                1 => Doubles(x => Math.Acos(x), x => MuslAcos(x)),
                2 => DoublePairs((x, y) => Math.Pow(x, y), (x, y) => MuslPow(x, y)),
                3 => Doubles(x => Math.Pow(10, x), x => MuslPow(10, x)) + Doubles(x => Math.Pow(x, 2), x => MuslPow(x, 2)) +
                    Doubles(x => Math.Pow(x, 0.5), x => MuslPow(x, 0.5)) + Doubles(x => Math.Pow(x, -1), x => MuslPow(x, -1)),
                4 => Doubles(x => double.Exp2(x), x => MuslExp2(x)),
                5 => Doubles(x => Math.SinCos(x).Sin, x => MuslSin(x)) + Doubles(x => Math.SinCos(x).Cos, x => MuslCos(x)),
                6 => Singles(x => MathF.Exp(x), x => MuslExpf(x)),
                7 => Singles(x => MathF.Log(x), x => MuslLogf(x)),
                8 => Singles(x => MathF.Log2(x), x => MuslLog2f(x)),
                9 => SinglePairs((x, y) => MathF.Pow(x, y), (x, y) => MuslPowf(x, y)),
                10 => Singles(x => MathF.Pow(10, x), x => MuslPowf(10, x)) + Singles(x => float.Exp10(x), x => MuslPowf(10, x)) +
                    Singles(x => MathF.Pow(x, 2), x => MuslPowf(x, 2)) + Singles(x => MathF.Pow(x, 0.5f), x => MuslPowf(x, 0.5f)),
                11 => Singles(x => MathF.Acosh(x), x => MuslAcoshf(x)),
                12 => Singles(x => MathF.Cbrt(x), x => MuslCbrtf(x)),
                13 => Singles(x => MathF.SinCos(x).Sin, x => MuslSinf(x)) + Singles(x => MathF.SinCos(x).Cos, x => MuslCosf(x)),
                _ => Singles(x => MathF.Log(Math.Abs(x)), x => MuslLogf(Math.Abs(x))) +
                    Singles(x => MathF.Log2(Math.Abs(x)), x => MuslLog2f(Math.Abs(x))),
            };

            private static readonly double[] SpecialDoubles =
            [
                0, -0.0, 1, -1, 2, -2, 0.5, -0.5, 3, 10, 1000, 0.1, 1e-300, 1e300, double.PositiveInfinity,
                double.NegativeInfinity, double.NaN, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
                Math.PI, Math.PI / 2, 709.78, -745.13, 1024, -1075, 0.9999999999999999, 1.0000000000000002,
                2.2250738585072014E-308, 0.7071067811865476, 1.4142135623730951,
            ];

            private static readonly float[] SpecialSingles =
            [
                0, -0f, 1, -1, 2, -2, 0.5f, -0.5f, 3, 10, 1000, 0.1f, float.PositiveInfinity, float.NegativeInfinity,
                float.NaN, float.Epsilon, -float.Epsilon, float.MaxValue, float.MinValue, MathF.PI, 88.72f, -103.97f,
                128, -150, 0.99999994f, 1.0000001f, 1.1754944E-38f, 0.70710677f, 1.4142135f,
            ];

            private static ulong s_state = 0x6A09E667F3BCC908UL;

            // splitmix64.
            private static ulong NextBits()
            {
                var z = s_state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }

            private static double NextUnit() => (NextBits() >> 11) * (1.0 / (1UL << 53));

            // Any bit pattern, everyday magnitudes, values near 1, and either sign across 120 binades.
            private static double NextDouble() => (NextBits() & 3) switch
            {
                0 => BitConverter.UInt64BitsToDouble(NextBits()),
                1 => (NextUnit() - 0.5) * 20,
                2 => 1 + (NextUnit() - 0.5) * Math.ScaleB(1, -(int)(NextBits() % 50)),
                _ => (NextUnit() - 0.5) * Math.ScaleB(1, (int)(NextBits() % 120) - 60),
            };

            private static bool Same(double port, double musl) =>
                BitConverter.DoubleToInt64Bits(port) == BitConverter.DoubleToInt64Bits(musl) ||
                (double.IsNaN(port) && double.IsNaN(musl));

            private static bool Same(float port, float musl) =>
                BitConverter.SingleToInt32Bits(port) == BitConverter.SingleToInt32Bits(musl) ||
                (float.IsNaN(port) && float.IsNaN(musl));

            private static int Doubles(Func<double, double> port, Func<double, double> musl)
            {
                var differ = 0;
                foreach (var x in SpecialDoubles)
                {
                    if (!Same(port(x), musl(x))) differ++;
                }
                for (var i = 0; i < 300_000; i++)
                {
                    var x = NextDouble();
                    if (!Same(port(x), musl(x))) differ++;
                }
                return differ;
            }

            private static int DoublePairs(Func<double, double, double> port, Func<double, double, double> musl)
            {
                var differ = 0;
                foreach (var x in SpecialDoubles)
                {
                    foreach (var y in SpecialDoubles)
                    {
                        if (!Same(port(x, y), musl(x, y))) differ++;
                    }
                }
                for (var i = 0; i < 300_000; i++)
                {
                    // Bases of either sign and of any size or below 16; exponents of any size, within 32, or integers.
                    var x = (i & 1) == 0 ? NextDouble() : Math.Abs(NextDouble()) % 16;
                    var y = (i & 6) switch
                    {
                        0 => NextDouble(),
                        2 => Math.Round((NextUnit() - 0.5) * 64),
                        _ => (NextUnit() - 0.5) * 64,
                    };
                    if (!Same(port(x, y), musl(x, y))) differ++;
                }
                return differ;
            }

            // A stride through all 2^32 floats, then the specials.
            private static int Singles(Func<float, float> port, Func<float, float> musl)
            {
                var differ = 0;
                for (var bits = 0UL; bits < 1UL << 32; bits += 4093)
                {
                    var x = BitConverter.UInt32BitsToSingle((uint)bits);
                    if (!Same(port(x), musl(x))) differ++;
                }
                foreach (var x in SpecialSingles)
                {
                    if (!Same(port(x), musl(x))) differ++;
                }
                return differ;
            }

            private static int SinglePairs(Func<float, float, float> port, Func<float, float, float> musl)
            {
                var differ = 0;
                foreach (var x in SpecialSingles)
                {
                    foreach (var y in SpecialSingles)
                    {
                        if (!Same(port(x, y), musl(x, y))) differ++;
                    }
                }
                for (var i = 0; i < 300_000; i++)
                {
                    var x = (i & 1) == 0 ? BitConverter.UInt32BitsToSingle((uint)NextBits()) : (float)(NextUnit() * 16);
                    var y = (i & 6) switch
                    {
                        0 => BitConverter.UInt32BitsToSingle((uint)NextBits()),
                        2 => MathF.Round((float)((NextUnit() - 0.5) * 64)),
                        _ => (float)((NextUnit() - 0.5) * 64),
                    };
                    if (!Same(port(x, y), musl(x, y))) differ++;
                }
                return differ;
            }
        }
        """;
}
