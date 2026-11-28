// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A loop that reads an element into a local and stores the local back, as `var t = a[0] + i; a[0] = t;` compiles
// without Roslyn's optimizations, after a store of the element before it, reaches its deepest stack at the stores
// (array, index, value). Forwarding the element through a local keeps a copy of each stored value with a dup there,
// one past that depth.
public sealed class LoopStoreForwardingAtMaxStackCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void AnElementStoredFromALocalAtTheDeepestPointIsForwarded(WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "LoopStoreForwardingAtMaxStackFixture",
            """
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Accumulate(int[] a, int start, int count)
                {
                    a[0] = start;
                    for (var i = 0; i < count; i++)
                    {
                        var t = a[0] + i;
                        a[0] = t;
                    }
                }

                public static int Run(int input)
                {
                    var a = new int[1];
                    Accumulate(a, input, 10);
                    return a[0];
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Equal(3 + 45, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 3, target, result.StaticDataEnd));
    }
}
