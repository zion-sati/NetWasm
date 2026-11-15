// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class BitArrayCompilationTests
{
    [Fact]
    public void CompilerExecutesNet11SpanConstructorsAndPopCount()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "BitArrayNet11Fixture",
            """
            using System;
            using System.Collections;

            namespace BitArrayNet11Fixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    ReadOnlySpan<bool> booleans = [true, false, true, true];
                    ReadOnlySpan<byte> bytes = [0xA5, 0x03];
                    ReadOnlySpan<int> integers = [unchecked((int)0x80000001), 3];

                    var booleanBits = new BitArray(booleans);
                    var byteBits = new BitArray(bytes);
                    var integerBits = new BitArray(integers);

                    return booleanBits.PopCount() * 100
                        + byteBits.PopCount() * 10
                        + integerBits.PopCount()
                        + input - input;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "BitArrayNet11Fixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(364, ExecuteWithNode(
            compilation.ApplicationModule,
            assets.Directory,
            41));
    }
}
