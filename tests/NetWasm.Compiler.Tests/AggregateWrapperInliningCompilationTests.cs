// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AggregateWrapperInliningCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DecimalOperatorsPreserveByValueArguments(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"AggregateWrapperInlining_{optimize}_{target}",
            Source,
            "EntryPoint",
            optimize,
            target,
            39,
            [])
        {
            Optimization = FinalWasmOptimization.O3,
        });

        Assert.Equal(42, observed);
    }

    private const string Source = """
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var left = (decimal)input;
                var right = 3m;
                var added = left + right;
                var subtracted = added - right;
                var multiplied = subtracted * 2m;
                var divided = multiplied / 2m;
                return left == divided ? 42 : 1;
            }
        }
        """;
}
