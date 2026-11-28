// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// float.SinCos and double.SinCos only pass their argument to MathF.SinCos and Math.SinCos, which ask to be inlined. At
// O3 a call to either is the call to its callee, so Matrix4x4's, Matrix3x2's and Quaternion's rotations, inlined into
// their callers, inline the pair too, and no caller keeps a value frame for the returned (Sin, Cos): kept a call, the
// pair came back through a frame each caller allocated.
public sealed class SinCosForwarderInliningCompilationTests
{
    private const string Source = """
        using System.Numerics;
        using System.Runtime.CompilerServices;

        public static class Rotations
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static float RotateX(float radians) => Matrix4x4.CreateRotationX(radians).M22;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static float RotateAxis(float radians) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians).W;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static float RotatePlane(float radians) => Matrix3x2.CreateRotation(radians).M11;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static float SinCosSingle(float x)
            {
                var (sin, cos) = float.SinCos(x);
                return sin + cos;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static double SinCosDouble(double x)
            {
                var (sin, cos) = double.SinCos(x);
                return sin + cos;
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var angle = (input - 42) * 0.5f;
                return (int)(Rotations.RotateX(angle) + Rotations.RotateAxis(angle) + Rotations.RotatePlane(angle) +
                    Rotations.SinCosSingle(angle) + Rotations.SinCosDouble(angle));
            }
        }
        """;

    // Each rotation and pair is inlined into its caller, which calls no SinCos; a caller whose only aggregate was the
    // returned pair enters no value frame (a matrix's own components still take one).
    [Fact]
    public void RotationsInlineTheirSinCosWithoutAValueFrameForThePair()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SinCosForwarders", Source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3));

        Assert.DoesNotContain(compilation.Program.Methods.Values, static method => method.Method.Definition.Name == "SinCos");
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, compilation.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var functions = Regex.Split(printed.StandardOutput, @"\n\s*\(func ")
            .Skip(1)
            .ToDictionary(static function => function[..function.IndexOfAny([' ', '\n'])], StringComparer.Ordinal);
        foreach (var caller in new[] { "RotateX", "RotateAxis", "RotatePlane", "SinCosSingle", "SinCosDouble" })
        {
            var body = functions["$Rotations::" + caller];
            Assert.DoesNotMatch(@"call \$\S*SinCos\b", body);
            if (caller is "RotateAxis" or "SinCosSingle" or "SinCosDouble")
            {
                Assert.DoesNotContain("value_frame_enter", body, StringComparison.Ordinal);
            }
        }
    }

    // At angle zero every rotation's component is cos 0 and each pair sums to sin 0 + cos 0: five ones.
    [Theory]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void RotationsComputeTheirComponents(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SinCosForwardersRun", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        Assert.Equal(5, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
