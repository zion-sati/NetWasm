// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// Activator.CreateInstance<T>() gives a value type without a parameterless constructor its zero value, in T's own Wasm
// type: an i64 for long, a long-backed enum and, on wasm64, nint and nuint; an f32 or f64 for float and double. The
// zero was always an i32, so the module failed to validate (an Avalonia program reached CreateInstance<nint>() on
// wasm64).
public sealed class ActivatorZeroValueCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void CreateInstanceGivesEachValueTypeItsOwnZero(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ActivatorZeroValueFixture",
            """
            using System;
            using System.Runtime.CompilerServices;

            public enum Wide : long { None = 0, Far = 1L << 40 }

            public struct Pair { public long A; public int B; }

            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Make<T>() => Activator.CreateInstance<T>();

                public static int Run(int input)
                {
                    var result = 0;
                    if (Make<long>() == 0L) result |= 1;
                    if (Make<double>() == 0.0 && !double.IsNegative(Make<double>())) result |= 2;
                    if (Make<float>() == 0f) result |= 4;
                    if (Make<nint>() == 0) result |= 8;
                    if (Make<nuint>() == 0) result |= 16;
                    if (Make<Wide>() == Wide.None) result |= 32;
                    if (Make<int>() == 0) result |= 64;
                    var pair = Make<Pair>();
                    if (pair.A == 0 && pair.B == 0) result |= 128;
                    return result * 10 + input;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(255 * 10 + 4, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 4, target, result.StaticDataEnd));
    }
}
