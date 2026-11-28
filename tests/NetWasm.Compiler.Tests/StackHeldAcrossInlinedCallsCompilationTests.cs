// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// Optimized Roslyn keeps a value read once on the evaluation stack while later calls run, as Svg.SceneGraph's
// SvgSceneFilterContext.GetDirection keeps the light's azimuth there across its elevation read and the first
// DegreeToRadian call (maxstack 3). At O3 and Oz the optimized method failed NW1002, its evaluation stack exceeding
// maxstack, where O0 compiled it.
public sealed class StackHeldAcrossInlinedCallsCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Os)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void AValueHeldOnTheStackAcrossInlinedCallsKeepsItsPlace(WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "StackHeldAcrossInlinedCallsFixture",
            """
            using System;

            public readonly struct Point3
            {
                public Point3(float x, float y, float z) { X = x; Y = y; Z = z; }
                public float X { get; }
                public float Y { get; }
                public float Z { get; }
            }

            public sealed class DistantLight
            {
                public float Azimuth { get; set; }
                public float Elevation { get; set; }
            }

            public static class Angles
            {
                public static double DegreeToRadian(double degrees) => Math.PI * degrees / 180.0;
            }

            public sealed class FilterContext
            {
                public Point3 GetDirection(DistantLight light)
                {
                    var azimuth = light.Azimuth;
                    var elevation = light.Elevation;
                    var a = Angles.DegreeToRadian(azimuth);
                    var e = Angles.DegreeToRadian(elevation);
                    var x = (float)(Math.Cos(a) * Math.Cos(e));
                    var y = (float)(Math.Sin(a) * Math.Cos(e));
                    var z = (float)Math.Sin(e);
                    return new Point3(x, y, z);
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var direction = new FilterContext().GetDirection(new DistantLight { Azimuth = 90, Elevation = 0 });
                    return (int)MathF.Round(direction.X * 100) * 10000 + (int)MathF.Round(direction.Y * 100) * 10 +
                        (int)MathF.Round(direction.Z * 100) + input;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        // Azimuth 90 and elevation 0 point along y: (0, 1, 0).
        Assert.Equal(1000 + 7, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 7, target, result.StaticDataEnd));
    }
}
