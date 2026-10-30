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
            public static int Run(int input)
            {
                // Every product is exact, so a fused and an unfused estimate agree.
                var f = Vector128.MultiplyAddEstimate(Vector128.Create(1.5f + input, -2f, 0.25f, 8f), Vector128.Create(4f), Vector128.Create(-3f, 0.5f, input, 1f));
                var d = Vector128.MultiplyAddEstimate(Vector128.Create(2.5 + input, -7.25), Vector128.Create(2.0, -4.0), Vector128.Create(1.0));
                var numerics = Vector.MultiplyAddEstimate(new Vector<float>(2f + input), new Vector<float>(0.5f), Vector<float>.One);
                var dn = Vector.MultiplyAddEstimate(new Vector<double>(-1.5 * input), new Vector<double>(6.0), new Vector<double>(0.125));
                return (int)(Vector128.Sum(f) * 7) + (int)(Vector128.Sum(d) * 5) + (int)(numerics[0] * 3) + (int)(dn[1] * 16);
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
        foreach (var instruction in new[] { "f64x2.promote_low_f32x4", "f32x4.demote_f64x2_zero", "f64x2.mul", "f64x2.add" })
        {
            Assert.Contains(instruction, printed.StandardOutput);
        }
        var call = SoftwareMultiplyAddCall().Match(printed.StandardOutput);
        Assert.False(call.Success, call.Value);

        // As desktop .NET computes them; every product is exact.
        foreach (var (input, expected) in new[] { (0, 394), (1, 296), (3, 101) })
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

    [GeneratedRegex(@"call \$[^\s]*Vector(128)?(`1)?[^\s]*::(op_Addition|op_Multiply|op_Subtraction|Equals|ExtractMostSignificantBits|Create)\b")]
    private static partial Regex SoftwareVectorCall();

    [GeneratedRegex(@"call \$[^\s]*Vector(64|128)?(`1)?[^\s]*::(op_Division|Divide|Sqrt|SquareRoot|Floor|Ceiling|Round|Truncate|Sum|ConvertTo\w+|WidenLower|WidenUpper|Narrow)\b")]
    private static partial Regex SoftwareVectorMathCall();
}
