// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NativeIntegerWideningCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void NativeWideningPreservesSignedAndUnsignedBoundaryValues(
        WasmTarget target,
        bool optimize)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "NativeIntegerWideningFixture",
            """
            using System;

            namespace NativeIntegerWideningFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var unsignedValues = new ulong[]
                    {
                        0, 1, 0x7fffffff, 0x80000000, 0xffffffff,
                        0x100000000, 0x7fffffffffffffff, 0x8000000000000000, ulong.MaxValue,
                    };
                    foreach (var value in unsignedValues)
                    {
                        var expected = UIntPtr.Size == 4 ? value & uint.MaxValue : value;
                        var native = unchecked((nuint)value);
                        if ((ulong)native != expected) return 1;
                        if (checked((ulong)native) != expected) return 2;
                    }
                    var signedValues = new long[]
                    {
                        long.MinValue, int.MinValue, -1, 0, 1, int.MaxValue, long.MaxValue,
                    };
                    foreach (var value in signedValues)
                    {
                        var expected = IntPtr.Size == 4 ? unchecked((int)value) : value;
                        var native = unchecked((nint)value);
                        if ((long)native != expected) return 3;
                        if (checked((long)native) != expected) return 4;
                    }
                    return input - input;
                }
            }
            """,
            "NativeIntegerWideningFixture.EntryPoint",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, result);
    }
}
