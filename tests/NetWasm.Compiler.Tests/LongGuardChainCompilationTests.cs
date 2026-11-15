// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class LongGuardChainCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void LongTerminatingGuardChainsCompileAndPreserveEveryExit(
        bool optimize, WasmTarget target, FinalWasmOptimization optimization)
    {
        const int count = 1536;
        using var assets = TestAssets.Create();
        var source = new StringBuilder("public static class EntryPoint { public static int Run(int input) {");
        for (var index = 0; index < count; index++)
            source.Append(CultureInfo.InvariantCulture, $"if (input == {index}) return {index};");
        source.Append("return -1; } }");
        var assembly = optimize
            ? assets.CompileOptimizedSource("LongGuardChain", source.ToString())
            : assets.CompileSource("LongGuardChain", source.ToString());
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: optimization));

        foreach (var input in new[] { -1, 0, count / 2, count - 1, count })
            Assert.Equal(input >= 0 && input < count ? input : -1,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory,
                    input, target, result.StaticDataEnd));
    }
}
