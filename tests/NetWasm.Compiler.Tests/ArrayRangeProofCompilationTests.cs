// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ArrayRangeProofCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Pairs(int[] values)
            {
                var sum = 0;
                for (var index = 0; index + 1 < values.Length; index++) sum += values[index] * 3 + values[index + 1];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Beyond(int[] values)
            {
                var sum = 0;
                for (var index = 0; index + 1 < values.Length; index++) sum += values[index + 2];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Last(int[] values) => values.Length > 0 ? values[^1] : -7;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Unguarded(int[] values) => values[^1];

            public static int Run(int count)
            {
                var values = new int[count];
                for (var index = 0; index < count; index++) values[index] = index * 5 + 1;
                var result = Pairs(values) * 31 + Last(values);
                // Each boundary keeps its exception.
                try { result += Beyond(values); } catch (IndexOutOfRangeException) { result += 1000; }
                try { result += Unguarded(values); } catch (IndexOutOfRangeException) { result += 2000; }
                try { result += Pairs(null!); } catch (NullReferenceException) { result += 4000; }
                try { result += Last(null!); } catch (NullReferenceException) { result += 8000; }
                return result;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void ProvenAccessesKeepResultsAndEveryUnprovenException(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("ArrayRangeProofs", Source) : assets.CompileSource("ArrayRangeProofs", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        foreach (var count in new[] { 0, 1, 2, 5 })
        {
            Assert.Equal(Expected(count), ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, count, target, result.StaticDataEnd));
        }
    }

    private static int Expected(int count)
    {
        var values = Enumerable.Range(0, count).Select(index => index * 5 + 1).ToArray();
        var pairs = 0;
        for (var index = 0; index + 1 < values.Length; index++) pairs += values[index] * 3 + values[index + 1];
        var result = pairs * 31 + (values.Length > 0 ? values[^1] : -7);
        // Beyond reads past the end whenever its loop runs; with fewer than two elements it returns 0.
        result += values.Length >= 2 ? 1000 : 0;
        result += values.Length == 0 ? 2000 : values[^1];
        return result + 4000 + 8000;
    }
}
