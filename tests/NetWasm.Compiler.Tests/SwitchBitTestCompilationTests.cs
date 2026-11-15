// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class SwitchBitTestCompilationTests
{
    private const string Shapes = """
        private static bool IsOdd(int value) => value is 1 or 3 or 5 or 7 or 9 or 11 or 13 or 15;
        private static bool IsSparse(int value) => value is 0 or 4 or 9 or 17 or 30 or 31;
        private static int Classify(int value) => value switch { 1 => 10, 2 => 20, 3 => 30, 4 => 40, 5 => 50, _ => -1 };
        private static bool IsNativeOdd(nint value) => value is 1 or 3 or 5 or 7 or 9;
        private static long Score(int value)
        {
            // A wide upper bound and the boundaries of both signed and unsigned ranges.
            var score = (IsOdd(value) ? 1 : 2) + (IsSparse(value) ? 4 : 8) + Classify(value) * 16;
            return score + (IsNativeOdd(value) ? 1024 : 2048) + (IsNativeOdd((nint)value + int.MaxValue) ? 4096 : 8192);
        }
        """;

    private static readonly int[] Inputs = [int.MinValue, -100, -33, -32, -31, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 15, 16, 17,
        29, 30, 31, 32, 33, 63, 64, 1000, int.MaxValue];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void BitTestsMatchTheCaseChains(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        var source = $$"""
            public static class EntryPoint
            {
                {{Shapes}}
                private static readonly int[] Values = [{{string.Join(", ", Inputs.Select(value => value == int.MinValue ? "int.MinValue" : value.ToString(System.Globalization.CultureInfo.InvariantCulture)))}}];
                public static int Run(int input)
                {
                    long checksum = 0;
                    foreach (var value in Values) checksum = checksum * 31 + Score(value + input);
                    return (int)(checksum ^ (checksum >> 32));
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("SwitchBitTests", source) : assets.CompileSource("SwitchBitTests", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        // Only the optimizing presets test bits; None keeps one compare per case.
        Assert.Equal(preset != FinalWasmOptimization.None, result.Program.Methods.Values.Any(body =>
            body.Body.Instructions.Any(instruction => instruction.Operation == CilOperation.ShiftRightUnsigned)));
        foreach (var input in new[] { 0, 1, -1 })
        {
            Assert.Equal(Expected(input), ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static int Expected(int input)
    {
        static bool IsOdd(int value) => value is 1 or 3 or 5 or 7 or 9 or 11 or 13 or 15;
        static bool IsSparse(int value) => value is 0 or 4 or 9 or 17 or 30 or 31;
        static int Classify(int value) => value switch { 1 => 10, 2 => 20, 3 => 30, 4 => 40, 5 => 50, _ => -1 };
        static bool IsNativeOdd(nint value) => value is 1 or 3 or 5 or 7 or 9;
        long Score(int value)
        {
            var score = (IsOdd(value) ? 1 : 2) + (IsSparse(value) ? 4 : 8) + Classify(value) * 16;
            // The shifted native value lies outside every case on both widths, as on this host.
            return score + (IsNativeOdd(value) ? 1024 : 2048) + (IsNativeOdd((nint)value + int.MaxValue) ? 4096 : 8192);
        }
        long checksum = 0;
        foreach (var value in Inputs) checksum = checksum * 31 + Score(unchecked(value + input));
        return (int)(checksum ^ (checksum >> 32));
    }
}
