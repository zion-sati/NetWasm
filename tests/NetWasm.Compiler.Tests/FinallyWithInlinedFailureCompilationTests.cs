// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A finally handler whose calls are inlined with a branch that never returns (Span<T>.CopyTo throws when the
// destination is too short) still ends with its endfinally: ImageSharp's IccProfile.CalculateHash restores the bytes
// it cleared this way, and its Release build failed structuring ("An endfinally does not end its handler").
public sealed class FinallyWithInlinedFailureCompilationTests
{
    private const string RestoreInFinally = """
        using System;
        using System.Runtime.CompilerServices;

        public static class Hasher
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Hash(byte[] data)
            {
                var total = 17;
                foreach (var value in data) total = total * 31 + value;
                return total;
            }

            public static int CalculateHash(byte[] data)
            {
                if (data.Length < 128) throw new ArgumentException("too short", nameof(data));
                Span<byte> temp = stackalloc byte[24];
                data.AsSpan(44, 4).CopyTo(temp);
                data.AsSpan(64, 4).CopyTo(temp.Slice(4));
                data.AsSpan(84, 16).CopyTo(temp.Slice(8));
                try
                {
                    Array.Clear(data, 44, 4);
                    Array.Clear(data, 64, 4);
                    Array.Clear(data, 84, 16);
                    return Hash(data);
                }
                finally
                {
                    temp.Slice(0, 4).CopyTo(data.AsSpan(44));
                    temp.Slice(4, 4).CopyTo(data.AsSpan(64));
                    temp.Slice(8, 16).CopyTo(data.AsSpan(84));
                }
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var data = new byte[160];
                for (var index = 0; index < data.Length; index++) data[index] = (byte)(index + input);
                var hash = Hasher.CalculateHash(data);
                // The finally restored the cleared bytes.
                return (hash & 0xFF) == 0 ? -1 : data[44] + data[64] + data[99] - 3 * input;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AFinallyWhoseInlinedCallsMayThrowStillEndsWithItsEndfinally(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assets.CompileOptimizedSource("FinallyWithInlinedFailureFixture", RestoreInFinally), [assets.CoreLib],
            "EntryPoint", "Run", [], Optimization: optimization));

        Assert.Equal(44 + 64 + 99, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 7,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
