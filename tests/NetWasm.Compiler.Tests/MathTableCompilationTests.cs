using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// The libm ports read their lookup tables from constant data, so a call checks
// no class initialization and loads no array reference before the table read.
// Each result is within 2 ulp of desktop's.
public sealed partial class MathTableCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            // As desktop .NET computes them.
            private static double[] Expected(int input) => input switch
            {
                0 => [1.1051709180756477, 1.0717734625362931, 1.2690587062858836, 0.8414709848078965, 1.1051709651947021, 0.09531020373106003, 0.13750354945659637, 1.1536897420883179],
                1 => [1.5999941932173602, 1.3851094681109246, 2.619952013052911, -0.9999232575641008, 1.59999418258667, 0.3852624297142029, 0.5558161735534668, 1.7822803258895874],
                3 => [3.3534846525490236, 2.3133763678105748, 7.260736636877831, -0.44964746453460147, 3.353484869003296, 0.7929925322532654, 1.144046425819397, 3.2854013442993164],
                7 => [14.73167592044257, 6.45313407377701, 26.155673978792823, 0.9807632477451529, 14.731677055358887, 1.3056265115737915, 1.8836208581924438, 7.088258743286133],
                _ => [2957929.2388223624, 30573.625066678112, 1008.0749218138502, -0.9746486480944947, 2957928, 2.7663190364837646, 3.990954875946045, 63.400936126708984],
            };

            private static bool Near(double actual, double expected) =>
                Math.Abs(BitConverter.DoubleToInt64Bits(actual) - BitConverter.DoubleToInt64Bits(expected)) <= 2;

            private static bool Near(float actual, double expected) =>
                Math.Abs(BitConverter.SingleToInt32Bits(actual) - BitConverter.SingleToInt32Bits((float)expected)) <= 2;

            // A bit per result that is further from desktop's.
            public static int Run(int input)
            {
                var x = input * 0.37 + 0.1;
                var f = (float)x;
                var expected = Expected(input);
                var mismatches = 0;
                if (!Near(Math.Exp(x), expected[0])) mismatches |= 1;
                if (!Near(double.Exp2(x), expected[1])) mismatches |= 2;
                if (!Near(Math.Pow(x + 1, 2.5), expected[2])) mismatches |= 4;
                if (!Near(Math.Sin(x * 10), expected[3])) mismatches |= 8;
                if (!Near(MathF.Exp(f), expected[4])) mismatches |= 16;
                if (!Near(MathF.Log(f + 1), expected[5])) mismatches |= 32;
                if (!Near(MathF.Log2(f + 1), expected[6])) mismatches |= 64;
                if (!Near(MathF.Pow(f + 1, 1.5f), expected[7])) mismatches |= 128;
                return mismatches;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    public void TablesNeedNoInitializationCheck(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"MathTable_{target}_{optimization}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var initializing = FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Where(static function => function.StartsWith("$System.Math", StringComparison.Ordinal) ||
                function.StartsWith("$EntryPoint::Run ", StringComparison.Ordinal))
            .Where(static function => StaticInitialization().IsMatch(function))
            .Select(static function => function[..function.IndexOf(' ', StringComparison.Ordinal)])
            .ToArray();
        Assert.Empty(initializing);
        foreach (var input in new[] { 0, 1, 3, 7, 40 })
        {
            Assert.Equal(0, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$static\.initialize")]
    private static partial Regex StaticInitialization();
}
