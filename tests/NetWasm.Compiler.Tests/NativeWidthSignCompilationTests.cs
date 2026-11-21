// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Math.Sign of a native integer reads the whole value on both widths: on wasm64 a value whose sign or magnitude
// lies above bit 31 (nint.MaxValue, nint.MinValue, half of either) has the sign desktop .NET gives it, directly
// and through nint.Sign. Each sign s adds (s + 1) * 3^i to the checksum, so the expected 587 + 587 * 2187 holds
// the same seven signs on both targets.
public sealed class NativeWidthSignCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SignOfANativeIntegerReadsTheWholeValue(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(587 + 587 * 2187, executor.Execute(new CompilationScenario(
            $"NativeWidthSign_{target}",
            Source,
            "EntryPoint",
            Optimize: true,
            target,
            0,
            [])));
    }

    private const string Source = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                nint[] values =
                [
                    nint.MaxValue - input, nint.MinValue + input, input + 1, -(nint)(input + 1), input,
                    nint.MaxValue / 2 + input, nint.MinValue / 2 - input,
                ];
                var direct = 0;
                var generic = 0;
                var weight = 1;
                foreach (var value in values)
                {
                    direct += (Math.Sign(value) + 1) * weight;
                    generic += (nint.Sign(value) + 1) * weight;
                    weight *= 3;
                }

                return direct + generic * 2187;
            }
        }
        """;
}
