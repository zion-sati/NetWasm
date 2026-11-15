// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class Utf8DecimalParsingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ParsesFractionalDecimalAndConsumesCompleteInput(WasmTarget target, bool optimize)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "Utf8DecimalParsing", Source, "Utf8DecimalParsing.EntryPoint",
            optimize, target, 41, [])
        { Optimization = FinalWasmOptimization.None });
        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        using System.Buffers.Text;
        namespace Utf8DecimalParsing;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (!Utf8Parser.TryParse("12.5000"u8, out decimal value, out var consumed))
                    return 1;
                if (consumed != 7)
                    return 2;
                if (value != 12.5m)
                    return 3;
                if (!Matches("12.5000"u8, 12.5000m, 7)) return 4;
                if (!Matches("-12.5"u8, -12.5m, 5)) return 5;
                if (!Matches("1e2"u8, 100m, 3)) return 6;
                if (!Matches("1e-28"u8, 0.0000000000000000000000000001m, 5)) return 7;
                if (!Matches("5e-29"u8, new decimal(0, 0, 0, false, 28), 5)) return 8;
                if (!Matches("15e-29"u8, 0.0000000000000000000000000002m, 6)) return 9;
                if (!Matches("-0.00"u8, new decimal(0, 0, 0, true, 2), 5)) return 10;
                if (!Matches("79228162514264337593543950335"u8, decimal.MaxValue, 29)) return 11;
                if (!Matches("-79228162514264337593543950335"u8, decimal.MinValue, 30)) return 12;
                if (!Matches("1.2x"u8, 1.2m, 3)) return 13;
                if (Utf8Parser.TryParse("79228162514264337593543950336"u8, out value, out consumed)
                    || consumed != 0) return 14;
                if (Utf8Parser.TryParse("1e29"u8, out value, out consumed)
                    || consumed != 0) return 15;
                if (Utf8Parser.TryParse("."u8, out value, out consumed)
                    || consumed != 0) return 16;
                return 42;
            }

            private static bool Matches(ReadOnlySpan<byte> source, decimal expected, int length)
            {
                if (!Utf8Parser.TryParse(source, out decimal actual, out var consumed)
                    || consumed != length) return false;
                var expectedBits = decimal.GetBits(expected);
                var actualBits = decimal.GetBits(actual);
                for (var index = 0; index < expectedBits.Length; index++)
                {
                    if (expectedBits[index] != actualBits[index]) return false;
                }
                return true;
            }
        }
        """;
}
