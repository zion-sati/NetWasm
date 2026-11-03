using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Vector128's math functions compute each lane as the scalar function does,
// within a few units in the last place: their polynomials fuse multiply-adds
// where the hardware can, so they need not match desktop bit for bit, but none
// may be lowered as something else, as Asin was lowered as a reinterpret for
// starting with As. Vector64, Vector256 and Vector512's Asin, which reach it
// through their own bodies, do too. Everything else of Vector128
// VectorSurfaceCorpusTests compares with desktop exactly.
public sealed class VectorMathFunctionCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            private static readonly double[] s_values =
                [0, -0.0, 0.5, -0.25, 1, -1, 0.999, 2.5, -3.75, 10, -20.5, 88, 700, -745, 1e-5, 1e20, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

            private static int s_check;
            private static int s_first;

            private static void Check(bool passed)
            {
                s_check++;
                if (!passed && s_first == 0) s_first = s_check;
            }

            // Within the units in the last place, or both NaN, or both the same infinity.
            private static bool Near(double lane, double scalar, long units) =>
                double.IsNaN(scalar) ? double.IsNaN(lane)
                    : double.IsInfinity(scalar) || scalar == 0 ? lane == scalar
                    : Math.Abs(BitConverter.DoubleToInt64Bits(lane) - BitConverter.DoubleToInt64Bits(scalar)) <= units;

            private static bool Near(float lane, float scalar, int units) =>
                float.IsNaN(scalar) ? float.IsNaN(lane)
                    : float.IsInfinity(scalar) || scalar == 0 ? lane == scalar
                    : Math.Abs(BitConverter.SingleToInt32Bits(lane) - BitConverter.SingleToInt32Bits(scalar)) <= units;

            private static Vector128<double> D(int i) => Vector128.Create(s_values[i], s_values[i + 1]);

            private static Vector128<double> D2(int i) => Vector128.Create(s_values[i + 2], s_values[i]);

            private static Vector128<float> F(int i) =>
                Vector128.Create((float)s_values[i], (float)s_values[i + 1], (float)-s_values[i], 0.75f);

            private static Vector128<float> F2(int i) =>
                Vector128.Create((float)s_values[i + 2], (float)s_values[i], 3f, 0.5f);

            // The vector function is called directly, where it lowers or keeps
            // its body; only the scalar goes through a delegate.
            private static void Unary(int i, Vector128<double> lanes, Func<double, double> scalar)
            {
                var input = D(i);
                Check(Near(lanes[0], scalar(input[0]), 8) && Near(lanes[1], scalar(input[1]), 8));
            }

            private static void Unary(int i, Vector128<float> lanes, Func<float, float> scalar)
            {
                var input = F(i);
                var passed = true;
                for (var lane = 0; lane < 4; lane++) passed &= Near(lanes[lane], scalar(input[lane]), 8);
                Check(passed);
            }

            private static void Binary(int i, Vector128<double> lanes, Func<double, double, double> scalar)
            {
                var (x, y) = (D(i), D2(i));
                Check(Near(lanes[0], scalar(x[0], y[0]), 8) && Near(lanes[1], scalar(x[1], y[1]), 8));
            }

            private static void Binary(int i, Vector128<float> lanes, Func<float, float, float> scalar)
            {
                var (x, y) = (F(i), F2(i));
                var passed = true;
                for (var lane = 0; lane < 4; lane++) passed &= Near(lanes[lane], scalar(x[lane], y[lane]), 8);
                Check(passed);
            }

            private static void Lanes(double[] input, double[] lanes, Func<double, double> scalar)
            {
                var passed = true;
                for (var lane = 0; lane < lanes.Length; lane++) passed &= Near(lanes[lane], scalar(input[lane]), 8);
                Check(passed);
            }

            private static void Lanes(float[] input, float[] lanes, Func<float, float> scalar)
            {
                var passed = true;
                for (var lane = 0; lane < lanes.Length; lane++) passed &= Near(lanes[lane], scalar(input[lane]), 8);
                Check(passed);
            }

            private static T[] A<T>(Vector64<T> vector)
            {
                var lanes = new T[Vector64<T>.Count];
                vector.CopyTo(lanes);
                return lanes;
            }

            private static T[] A<T>(Vector256<T> vector)
            {
                var lanes = new T[Vector256<T>.Count];
                vector.CopyTo(lanes);
                return lanes;
            }

            private static T[] A<T>(Vector512<T> vector)
            {
                var lanes = new T[Vector512<T>.Count];
                vector.CopyTo(lanes);
                return lanes;
            }

            // 0 when every lane is near its scalar, or the first failing check's number.
            public static int Run(int input)
            {
                for (var i = 0; i + 2 < s_values.Length; i++)
                {
                    Unary(i, Vector128.Asin(D(i)), Math.Asin);
                    Unary(i, Vector128.Asin(F(i)), MathF.Asin);
                    Unary(i, Vector128.Sin(D(i)), Math.Sin);
                    Unary(i, Vector128.Sin(F(i)), MathF.Sin);
                    Unary(i, Vector128.Cos(D(i)), Math.Cos);
                    Unary(i, Vector128.Cos(F(i)), MathF.Cos);
                    Unary(i, Vector128.Exp(D(i)), Math.Exp);
                    Unary(i, Vector128.Exp(F(i)), MathF.Exp);
                    Unary(i, Vector128.Log(D(i)), Math.Log);
                    Unary(i, Vector128.Log(F(i)), MathF.Log);
                    Unary(i, Vector128.Log2(D(i)), Math.Log2);
                    Unary(i, Vector128.Log2(F(i)), MathF.Log2);
                    Unary(i, Vector128.DegreesToRadians(D(i)), double.DegreesToRadians);
                    Unary(i, Vector128.DegreesToRadians(F(i)), float.DegreesToRadians);
                    Unary(i, Vector128.RadiansToDegrees(D(i)), double.RadiansToDegrees);
                    Unary(i, Vector128.RadiansToDegrees(F(i)), float.RadiansToDegrees);
                    Binary(i, Vector128.Hypot(D(i), D2(i)), double.Hypot);
                    Binary(i, Vector128.Hypot(F(i), F2(i)), float.Hypot);
                    Binary(i, Vector128.Lerp(D(i), D2(i), Vector128.Create(0.25)), static (x, y) => double.Lerp(x, y, 0.25));
                    Binary(i, Vector128.Lerp(F(i), F2(i), Vector128.Create(0.25f)), static (x, y) => float.Lerp(x, y, 0.25f));
                    var narrow = Vector64.Create(s_values[i]);
                    Lanes(A(narrow), A(Vector64.Asin(narrow)), Math.Asin);
                    var narrowSingle = Vector64.Create((float)s_values[i], (float)s_values[i + 1]);
                    Lanes(A(narrowSingle), A(Vector64.Asin(narrowSingle)), MathF.Asin);
                    var wide = Vector256.Create(D(i), D2(i));
                    Lanes(A(wide), A(Vector256.Asin(wide)), Math.Asin);
                    var wideSingle = Vector256.Create(F(i), F2(i));
                    Lanes(A(wideSingle), A(Vector256.Asin(wideSingle)), MathF.Asin);
                    var wider = Vector512.Create(wide, Vector256.Create(D2(i), D(i)));
                    Lanes(A(wider), A(Vector512.Asin(wider)), Math.Asin);
                    var widerSingle = Vector512.Create(wideSingle, Vector256.Create(F2(i), F(i)));
                    Lanes(A(widerSingle), A(Vector512.Asin(widerSingle)), MathF.Asin);
                }
                return s_first;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void MathFunctionsComputeEveryLane(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("VectorMathFunctions", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        Assert.Equal(0,
            ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }
}
