// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BigIntegerRoundingRegressionTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void IntegralHighPrecisionRoundingPreservesItsExactQuotient(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Numerics;

            namespace IntegralRoundingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                        var numerator = new BigInteger(1UL << 52);
                        var denominator = BigInteger.One << 52;
                        var scale = BigInteger.Pow(new BigInteger(10), 16);
                        numerator *= scale;
                        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
                        if (remainder != BigInteger.Zero || quotient != scale) return 106;
                        var text = quotient.ToString();
                        if (text != "10000000000000000") return 107;
                        if (text.ToCharArray().Length != 17) return 108;
                        if (Math.Round(1.0, 16) != 1.0) return 109;
                        if (Math.Round(-1.0, 16) != -1.0) return 110;
                        if (MathF.Round(1.0f, 7) != 1.0f) return 111;
                        if (MathF.Round(-1.0f, 7) != -1.0f) return 112;
                        foreach (var dividend in new[] { -16, -9, -8, -4, 0, 4, 8, 9, 16 })
                        foreach (var divisor in new[] { -4, -2, 2, 4 })
                        {
                            var q = BigInteger.DivRem(new BigInteger(dividend), new BigInteger(divisor), out var r);
                            if (q != new BigInteger(dividend / divisor) || r != new BigInteger(dividend % divisor)) return 113;
                        }
                        return 0;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "IntegralRoundingFixture", source, "IntegralRoundingFixture.EntryPoint",
            optimize, target, 0, []));

        Assert.Equal(0, result);
    }
}
