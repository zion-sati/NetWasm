// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ConstantArgumentLoopCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void FoldedEntryBranchPreservesLoopHeaderAndZeroTripBehavior(
        bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        const string source = """
            public static class EntryPoint
            {
                private static bool IsZero(int mode) => mode == 0;
                private static int Sum(int count, int mode)
                {
                    var i = 0;
                    var total = 0;
                    if (IsZero(mode)) goto Test;
                    goto Body;
                Body:
                    total += ++i;
                Test:
                    if (i < count) goto Body;
                    return total;
                }
                public static int Run(int count) => Sum(count, 0);
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("ConstantLoopEntry", source)
            : assets.CompileSource("ConstantLoopEntry", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly,
            [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: preset));

        Assert.Equal(0, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
        Assert.Equal(10, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 4, target, result.StaticDataEnd));
    }
}
