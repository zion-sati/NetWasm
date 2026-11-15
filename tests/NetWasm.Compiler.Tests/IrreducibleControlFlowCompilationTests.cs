// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class IrreducibleControlFlowCompilationTests
{
    [Fact]
    public void CompilerExecutesAnIrreducibleDispatcherThatOverlapsANaturalLoop()
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace IrreducibleDispatcherFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input + 5;
                    var steps = 0;
                    if (((input + 1) & 1) == 0) goto Left;
                    goto Right;
                Left:
                    value = unchecked(value + steps + 3);
                    steps++;
                    if (steps >= 5) goto Exit;
                    goto Right;
                Right:
                    value = unchecked(value ^ (steps + 10));
                    steps++;
                    if (steps >= 5) goto Exit;
                    if ((value & 1) == 0) goto Left;
                    goto Right;
                Exit:
                    return value;
                }
            }
            """;
        var debug = assets.CompileSource("IrreducibleDispatcherDebug", source);
        var release = assets.CompileOptimizedSource(
            "IrreducibleDispatcherRelease",
            source);

        foreach (var assembly in (string[])[debug, release])
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "IrreducibleDispatcherFixture.EntryPoint",
                "Run",
                []));

            Assert.Equal(11, CompilerTestSupport.ExecuteWithNode(
                result.ApplicationModule,
                assets.Directory,
                0));
        }
    }
}
