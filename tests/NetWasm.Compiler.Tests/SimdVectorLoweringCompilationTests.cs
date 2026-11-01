using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Vector128<T> and Vector<T> operations ran upstream's software implementation,
// a loop over the elements per operation. They lower to Wasm SIMD instructions
// at every preset, and call no managed implementation of those operations.
public sealed partial class SimdVectorLoweringCompilationTests
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new int[64];
                for (var i = 0; i < values.Length; i++) values[i] = i * 7 - input;
                var sum = Vector128<int>.Zero;
                for (var i = 0; i + 4 <= values.Length; i += 4)
                    sum += Vector128.Create(values[i], values[i + 1], values[i + 2], values[i + 3]) * Vector128.Create(3);
                var floats = Vector128.Create(1.5f + input) * Vector128.Create(2f) - Vector128.Create(0.25f);
                var bytes = Vector128.Equals(Vector128.Create((byte)input), Vector128.Create((byte)3));
                var numerics = new Vector<double>(input) + Vector<double>.One;
                return sum.GetElement(0) + sum.GetElement(3) * 3 + (int)floats.GetElement(1) +
                    (int)Vector128.ExtractMostSignificantBits(bytes) + (int)numerics[0];
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void VectorOperationsLowerToWasmSimd(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdVectorLowering", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        foreach (var instruction in new[]
                 {
                     "i32x4.add", "i32x4.mul", "i32x4.splat", "f32x4.mul", "f32x4.sub",
                     "i8x16.eq", "i8x16.bitmask", "f64x2.add", "f64x2.splat", "i32x4.replace_lane",
                 })
        {
            Assert.Contains(instruction, printed.StandardOutput);
        }
        var call = SoftwareVectorCall().Match(printed.StandardOutput);
        Assert.False(call.Success, call.Value);

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 43347), (1, 43158), (3, 108315) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // Division, square roots, rounding, sums, conversions, widening and
    // narrowing, whose software implementations split the vector into halves and
    // loop over the elements.
    private const string MathSource = """
        using System;
        using System.Numerics;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var f = Vector128.Create(1.5f + input, -2.5f, 9f, input * 0.25f);
                var d = Vector128.Create(2.5 + input, -7.25);
                var ints = Vector128.Create(100 + input, -7, 30, 41);
                var quotient = f / Vector128.Create(2f) + Vector128.Sqrt(Vector128.Abs(f)) +
                    Vector128.Floor(f) + Vector128.Ceiling(f) + Vector128.Round(f) + Vector128.Truncate(f);
                var wide = Vector128.Sqrt(d) / d + Vector128.Floor(d);
                var converted = Vector128.ConvertToInt32(quotient) + Vector128.ConvertToInt32(Vector128.ConvertToSingle(ints));
                var widened = Vector128.WidenLower(Vector128.Create((byte)(200 + input))) + Vector128.WidenUpper(Vector128.Create((byte)7));
                var doubled = Vector128.WidenLower(f) + Vector128.WidenUpper(f);
                var narrowed = Vector128.Narrow(widened, widened);
                var divided = ints / Vector128.Create(3, -2, 5, 7);
                var numerics = Vector.SquareRoot(new Vector<float>(16f + input)) / new Vector<float>(2f);
                return Vector128.Sum(converted) + Vector128.Sum(divided) + (int)Vector128.Sum(wide) + (int)Vector128.Sum(doubled) +
                    Vector128.Sum(narrowed) + (int)Vector128.Dot(f, f) + (int)numerics[0];
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void VectorMathLowersToWasmSimd(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdVectorMathLowering", MathSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        foreach (var instruction in new[]
                 {
                     "f32x4.div", "f32x4.sqrt", "f32x4.floor", "f32x4.ceil", "f32x4.nearest", "f32x4.trunc",
                     "f64x2.sqrt", "f64x2.div", "f64x2.floor", "i32x4.trunc_sat_f32x4_s", "f32x4.convert_i32x4_s",
                     "i16x8.extend_low_i8x16_u", "i16x8.extend_high_i8x16_u", "f64x2.promote_low_f32x4", "i8x16.shuffle",
                 })
        {
            Assert.Contains(instruction, printed.StandardOutput);
        }
        var call = SoftwareVectorMathCall().Match(printed.StandardOutput);
        Assert.False(call.Success, call.Value);

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 592), (1, 363), (3, 427) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // MultiplyAddEstimate ran upstream's software implementation, element by
    // element through Vector64 halves. Float lanes widen to double, where the
    // product is exact, so the sum rounds once, as float's estimate and desktop's
    // fused form do; double lanes multiply and then add, as upstream's estimate
    // does without FMA hardware and as Mono computes it.
    private const string MultiplyAddSource = """
        using System;
        using System.Numerics;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            // 1 + 2^-12, whose square needs one bit more than a float holds.
            private static float s_near = 1f + 1f / 4096;

            public static int Run(int input)
            {
                // Every product is exact, so a fused and an unfused estimate agree.
                var f = Vector128.MultiplyAddEstimate(Vector128.Create(1.5f + input, -2f, 0.25f, 8f), Vector128.Create(4f), Vector128.Create(-3f, 0.5f, input, 1f));
                var d = Vector128.MultiplyAddEstimate(Vector128.Create(2.5 + input, -7.25), Vector128.Create(2.0, -4.0), Vector128.Create(1.0));
                var numerics = Vector.MultiplyAddEstimate(new Vector<float>(2f + input), new Vector<float>(0.5f), Vector<float>.One);
                var dn = Vector.MultiplyAddEstimate(new Vector<double>(-1.5 * input), new Vector<double>(6.0), new Vector<double>(0.125));
                // The product rounds first, as Mono computes it: s_near squared rounds
                // to 1 + 2^-11, so the estimate is exactly 2^-11. Fused, it would be
                // 2^-11 + 2^-24.
                var rounded = Vector128.MultiplyAddEstimate(Vector128.Create(s_near), Vector128.Create(s_near), Vector128.Create(-1f));
                var scalar = float.MultiplyAddEstimate(s_near, s_near, -1f);
                return (int)(Vector128.Sum(f) * 7) + (int)(Vector128.Sum(d) * 5) + (int)(numerics[0] * 3) + (int)(dn[1] * 16) +
                    (rounded.GetElement(2) == 1f / 2048 ? 1000 : 0) + (scalar == 1f / 2048 ? 2000 : 0);
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void VectorMultiplyAddEstimateLowersToWasmSimd(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdVectorMultiplyAdd", MultiplyAddSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        foreach (var instruction in new[] { "f32x4.mul", "f32x4.add", "f64x2.mul", "f64x2.add" })
        {
            Assert.Contains(instruction, printed.StandardOutput);
        }
        Assert.DoesNotContain("f64x2.promote_low_f32x4", printed.StandardOutput);
        var call = SoftwareMultiplyAddCall().Match(printed.StandardOutput);
        Assert.False(call.Success, call.Value);

        // As Mono computes them: the exact products agree with desktop, and the
        // inexact one rounds before the addend, where desktop fuses.
        foreach (var (input, expected) in new[] { (0, 3394), (1, 3296), (3, 3101) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // A vector times a scalar, in either order, and the LessThan, GreaterThan,
    // LessThanOrEqual and GreaterThanOrEqual All and Any predicates ran
    // upstream's software implementation, element by element through Vector64
    // halves.
    private const string CompareSource = """
        using System;
        using System.Numerics;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var ints = Vector128.Create(input, -3, 7, 100);
                var others = Vector128.Create(2, -3, 9, 50);
                var bytes = Vector128.Create((byte)(250 + input)) * (byte)3 + (byte)5 * Vector128.Create((byte)(input + 1));
                var longs = Vector128.Create(5L + input, long.MinValue + 3) * -7L;
                var floats = 0.5f * Vector128.Create(1.5f + input, -2f, float.NaN, 8f) * 3f;
                var doubles = Vector128.Create(2.5 + input, -7.25) * 4.0;
                var unsigned = Vector128.Create(uint.MaxValue - (uint)input, 3u, 9u, 1u);
                var wide = Vector128.Create(ulong.MaxValue, (ulong)input);
                var numerics = 2f * new Vector<float>(input) * 0.25f;
                var bits = 0;
                bool[] tests =
                [
                    Vector128.LessThanAll(ints, others), Vector128.LessThanAny(ints, others),
                    Vector128.LessThanOrEqualAll(others, others), Vector128.LessThanOrEqualAny(ints, others),
                    Vector128.GreaterThanAll(ints, others), Vector128.GreaterThanAny(ints, others),
                    Vector128.GreaterThanOrEqualAll(ints, others), Vector128.GreaterThanOrEqualAny(others, ints),
                    Vector128.LessThanAny(unsigned, Vector128.Create(5u)), Vector128.GreaterThanAll(unsigned, Vector128<uint>.Zero),
                    Vector128.GreaterThanAny(wide, Vector128.Create(2UL)), Vector128.LessThanAll(wide, Vector128.Create(ulong.MaxValue)),
                    Vector128.LessThanAll(floats, Vector128.Create(100f)), Vector128.GreaterThanOrEqualAny(floats, Vector128.Create(12f)),
                    Vector128.LessThanOrEqualAll(doubles, Vector128.Create(20.0)), Vector128.GreaterThanAny(bytes, Vector128.Create((byte)200)),
                    Vector.LessThanAll(numerics, new Vector<float>(1f)), Vector.GreaterThanOrEqualAny(numerics, Vector<float>.Zero),
                ];
                for (var index = 0; index < tests.Length; index++) bits |= tests[index] ? 1 << index : 0;
                return bits * 31 + bytes.GetElement(3) + (int)(longs.GetElement(1) >> 40) + (int)floats.GetElement(3) +
                    (int)doubles.GetElement(0) + (int)numerics[1];
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ScalarProductsAndComparisonPredicatesLowerToWasmSimd(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdVectorCompare", CompareSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        foreach (var instruction in new[]
                 {
                     "i64x2.mul", "f64x2.mul", "i16x8.extmul_low_i8x16_u", "i32x4.lt_s", "i32x4.all_true",
                     "v128.any_true", "i32x4.gt_u", "f32x4.ge",
                 })
        {
            Assert.Contains(instruction, printed.StandardOutput);
        }
        var call = SoftwareComparisonCall().Match(printed.StandardOutput);
        Assert.False(call.Success, call.Value);

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 16322330), (1, 16322342), (3, 12766783) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"call \$[^\s]*(Vector(64|128)?(`1)?|Scalar`1)[^\s]*::(op_Multiply|Multiply|(LessThan|GreaterThan)(OrEqual)?(All|Any)?)\b")]
    private static partial Regex SoftwareComparisonCall();

    [GeneratedRegex(@"call \$[^\s]*Vector(64|128)?(`1)?[^\s]*::MultiplyAddEstimate\b")]
    private static partial Regex SoftwareMultiplyAddCall();

    // Vector values are v128 values in locals: a chain of operations on locals
    // reads and writes no memory, where each result went through a value-frame
    // temporary with v128.store and v128.load.
    private const string LocalsSource = """
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var a = Vector128.Create(input);
                var b = Vector128.Create(input + 1, 2, 3, 4);
                var c = (a + b) * b - a;
                c = Vector128.Max(c, a) ^ b;
                return Vector128.Sum(c);
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void VectorLocalsStayInV128Locals(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdVectorLocals", LocalsSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var run = EntryFunction().Match(printed.StandardOutput);
        Assert.True(run.Success, "EntryPoint::Run is missing from the module");
        Assert.Contains("v128", run.Value);
        Assert.DoesNotContain("v128.load", run.Value);
        Assert.DoesNotContain("v128.store", run.Value);

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 36), (1, 45), (3, 75) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\(func \$EntryPoint::Run .*?(?=\n  \(func |\n  \(export |\z)", RegexOptions.Singleline)]
    private static partial Regex EntryFunction();

    // Vector128.Shuffle of lanes wider than a byte ran upstream's software
    // implementation, a loop that reads and writes each element through memory;
    // SpanHelpers.Reverse shuffles every vector it reverses. Every lane width
    // lowers to i8x16.swizzle by byte indices.
    private const string ShuffleSource = """
        using System;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var ints = Vector128.Shuffle(Vector128.Create(10, 20, 30, 40), Vector128.Create(3, input, 9, -1));
                var shorts = Vector128.Shuffle(Vector128.Create((short)1, 2, 3, 4, 5, 6, 7, 8), Vector128.Create((short)7, 6, 5, 4, 3, 2, 1, 0));
                var longs = Vector128.Shuffle(Vector128.Create(5L, 6L), Vector128.Create(1L, input));
                var floats = Vector128.Shuffle(Vector128.Create(1f, 2f, 3f, 4f), Vector128.Create(0, 0, input, 2));
                var bytes = Vector128.Shuffle(Vector128.Create((byte)input), Vector128.Create((byte)0, 16, 1, 255, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));
                var values = new int[37];
                for (var i = 0; i < values.Length; i++) values[i] = i * 3 - input;
                values.AsSpan().Reverse();
                return ints.GetElement(0) + ints.GetElement(1) * 3 + ints.GetElement(2) * 5 + ints.GetElement(3) * 7
                    + shorts.GetElement(0) * 11 + (int)longs.GetElement(1) * 13 + (int)floats.GetElement(2) * 17
                    + bytes.GetElement(0) + bytes.GetElement(1) * 19 + values[0] * 23 + values[36] * 29;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ShufflesOfEveryLaneWidthLowerToSwizzle(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdShuffleLowering", ShuffleSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        Assert.Contains("i8x16.swizzle", printed.StandardOutput);
        Assert.DoesNotMatch(@"ShuffleFallback|call \$[^\s]*Vector128[^\s]*::Shuffle\b", printed.StandardOutput);

        // As desktop .NET computes them: lane 9 and -1 read zero, as do byte lanes 16 and 255.
        foreach (var (input, expected) in new[] { (0, 2724), (1, 2733), (2, 2651), (3, 2647) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // Upstream's Vector2, Vector3, Vector4 and matrix code reads lanes with a
    // constant GetElement index. Vectors of up to four lanes select among their
    // lanes' extracts, so a constant index leaves one extract_lane, where a byte
    // swizzle by the index kept a swizzle and a constant load.
    private const string ElementSource = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var v = Vector128.Create(1.5f + input, 2.5f, 3.5f, 4.5f) * Vector128.Create(input + 1f);
                var longs = Vector128.Create(10L + input, 20L);
                var broadcast = Vector128.Create(v.GetElement(2));
                return (int)(v.GetElement(1) * 10) + (int)broadcast.GetElement(3) + (int)longs.GetElement(1) +
                    Variable(v, input & 3);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Variable(Vector128<float> vector, int index) => (int)vector.GetElement(index);
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConstantElementIndicesExtractTheirLane(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdElementLowering", ElementSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var run = Regex.Match(printed.StandardOutput, @"\(func \$EntryPoint::Run [\s\S]*?(?=\n  \(func |\z)").Value;
        Assert.Contains("f32x4.extract_lane", run);
        Assert.DoesNotContain("i8x16.swizzle", run);

        // As desktop .NET computes them, with the variable index reading lane input & 3.
        foreach (var (input, expected) in new[] { (0, 49), (1, 82), (3, 152) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    // Upstream builds ZipLower, UnzipEven, ConcatLowerUpper and the rest from
    // Vector64 halves, which NetWasm runs in software through memory; Matrix4x4's
    // Transpose and Invert are made of them. Each is one byte shuffle for its lane
    // width. The program checks every one against the lanes it should pick, built
    // element by element, so it returns the count of wrong lanes times 1000 plus
    // the scalars it creates.
    private const string PermuteSource = """
        using System;
        using System.Numerics;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var wrong = Check<byte>(input) + Check<ushort>(input) + Check<int>(input) + Check<long>(input) +
                    Check<float>(input) + Check<double>(input);
                var a = new Vector<int>(input);
                var b = Vector<int>.Indices;
                wrong += Mismatches(Vector.ZipLower(a, b).AsVector128(), [input, 0, input, 1]) +
                    Mismatches(Vector.ConcatUpperLower(a, b).AsVector128(), [input, input, 0, 1]);
                var scalar = Vector128.CreateScalar(input + 5);
                var single = Vector128.CreateScalar(2.5f * input);
                var numerics = Vector.CreateScalar((short)(input + 9));
                var anyUpper = Vector128.CreateScalarUnsafe(input * 3L);
                return wrong * 1000 + scalar.GetElement(0) + (scalar.GetElement(1) + scalar.GetElement(3)) * 7 +
                    (int)single.GetElement(0) + (int)single.GetElement(2) * 11 + numerics[0] + numerics[7] * 13 +
                    (int)anyUpper.GetElement(0);
            }

            private static int Check<T>(int input) where T : INumber<T>
            {
                var count = Vector128<T>.Count;
                var half = count / 2;
                var left = new T[count];
                var right = new T[count];
                for (var i = 0; i < count; i++)
                {
                    left[i] = T.CreateTruncating(i + 1 + input);
                    right[i] = T.CreateTruncating(100 + i);
                }
                var l = Vector128.Create(left);
                var r = Vector128.Create(right);
                var zipLower = new T[count];
                var zipUpper = new T[count];
                var unzipEven = new T[count];
                var unzipOdd = new T[count];
                var lowerLower = new T[count];
                var lowerUpper = new T[count];
                var upperLower = new T[count];
                var upperUpper = new T[count];
                for (var i = 0; i < half; i++)
                {
                    zipLower[2 * i] = left[i];
                    zipLower[2 * i + 1] = right[i];
                    zipUpper[2 * i] = left[half + i];
                    zipUpper[2 * i + 1] = right[half + i];
                    unzipEven[i] = left[2 * i];
                    unzipEven[half + i] = right[2 * i];
                    unzipOdd[i] = left[2 * i + 1];
                    unzipOdd[half + i] = right[2 * i + 1];
                    lowerLower[i] = left[i];
                    lowerLower[half + i] = right[i];
                    lowerUpper[i] = left[i];
                    lowerUpper[half + i] = right[half + i];
                    upperLower[i] = left[half + i];
                    upperLower[half + i] = right[i];
                    upperUpper[i] = left[half + i];
                    upperUpper[half + i] = right[half + i];
                }
                return Mismatches(Vector128.ZipLower(l, r), zipLower) + Mismatches(Vector128.ZipUpper(l, r), zipUpper) +
                    Mismatches(Vector128.UnzipEven(l, r), unzipEven) + Mismatches(Vector128.UnzipOdd(l, r), unzipOdd) +
                    Mismatches(Vector128.ConcatLowerLower(l, r), lowerLower) +
                    Mismatches(Vector128.ConcatLowerUpper(l, r), lowerUpper) +
                    Mismatches(Vector128.ConcatUpperLower(l, r), upperLower) +
                    Mismatches(Vector128.ConcatUpperUpper(l, r), upperUpper);
            }

            private static int Mismatches<T>(Vector128<T> actual, T[] expected) where T : INumber<T>
            {
                var wrong = 0;
                for (var i = 0; i < expected.Length; i++)
                {
                    if (actual.GetElement(i) != expected[i]) wrong++;
                }
                return wrong;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void LanePermutationsAndScalarsLowerToShuffles(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SimdPermuteLowering", PermuteSource);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        Assert.Contains("i8x16.shuffle", printed.StandardOutput);
        Assert.DoesNotMatch(
            @"call \$[^\s]*Vector(64|128)?(`1)?[^\s]*::(ZipLower|ZipUpper|UnzipEven|UnzipOdd|Concat\w+|CreateScalar\w*|GetLower|GetUpper)\b",
            printed.StandardOutput);

        // No wrong lane; CreateScalar zeroes the lanes above the first.
        foreach (var (input, expected) in new[] { (0, 5 + 9), (1, 6 + 2 + 10 + 3), (4, 9 + 10 + 13 + 12) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"call \$[^\s]*Vector(128)?(`1)?[^\s]*::(op_Addition|op_Multiply|op_Subtraction|Equals|ExtractMostSignificantBits|Create)\b")]
    private static partial Regex SoftwareVectorCall();

    [GeneratedRegex(@"call \$[^\s]*Vector(64|128)?(`1)?[^\s]*::(op_Division|Divide|Sqrt|SquareRoot|Floor|Ceiling|Round|Truncate|Sum|ConvertTo\w+|WidenLower|WidenUpper|Narrow)\b")]
    private static partial Regex SoftwareVectorMathCall();
}
