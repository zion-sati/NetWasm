using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Math.SinCos and MathF.SinCos inline into their callers, which keep both
// results as values. A SinCos that did not would return its pair through a
// buffer in a value frame its caller allocates on every call.
public sealed partial class SinCosFrameCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double Double(double value)
            {
                var (sin, cos) = Math.SinCos(value);
                return sin * 3 + cos;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Single(float value)
            {
                var (sin, cos) = MathF.SinCos(value);
                return sin * 3 + cos;
            }

            public static int Run(int input)
            {
                var value = input * 0.75;
                if (Double(value) != Math.Sin(value) * 3 + Math.Cos(value)) return -1;
                if (Single((float)value) != MathF.Sin((float)value) * 3 + MathF.Cos((float)value)) return -2;
                return input + 1;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SinCosCallersNeedNoValueFrame(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"SinCosFrame_{target}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O3));

        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var framed = FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ', StringComparison.Ordinal)], Body: function))
            .Where(static function => function.Name is "$EntryPoint::Double" or "$EntryPoint::Single")
            .ToDictionary(static function => function.Name, static function => FrameEnter().IsMatch(function.Body));
        Assert.Equal(new Dictionary<string, bool> { ["$EntryPoint::Double"] = false, ["$EntryPoint::Single"] = false }, framed);
        foreach (var input in new[] { 0, 1, 3, 40, 1000000 })
        {
            Assert.Equal(input + 1, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$\S*value_frame_enter")]
    private static partial Regex FrameEnter();
}
