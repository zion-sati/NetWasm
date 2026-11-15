// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static NetWasm.Compiler.Tests.CompilerTestSupport;

public sealed class StringAsSpanCompilationTests
{
    [Fact]
    public void CompilerExecutesAllStringAsSpanOverloads()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "StringAsSpanFixture",
            """
            namespace StringAsSpanFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    const string source = "ABCD";
                    if (input == 1)
                    {
                        var span = System.MemoryExtensions.AsSpan(source);
                        return span.Length * 100 + span[1];
                    }
                    if (input == 2)
                    {
                        var span = System.MemoryExtensions.AsSpan(source, 1);
                        return span.Length * 100 + span[0];
                    }
                    if (input == 3)
                    {
                        var span = System.MemoryExtensions.AsSpan(source, 1, 2);
                        return span.Length * 100 + span[1];
                    }
                    if (input == 4)
                    {
                        var span = System.MemoryExtensions.AsSpan(source, System.Index.FromEnd(2));
                        return span.Length * 100 + span[0];
                    }
                    if (input == 5)
                    {
                        var span = System.MemoryExtensions.AsSpan(source, new System.Range(1, System.Index.FromEnd(1)));
                        return span.Length * 100 + span[1];
                    }
                    if (input == 6)
                    {
                        var span = System.MemoryExtensions.AsSpan((string?)null);
                        return span.IsEmpty ? 1 : 0;
                    }
                    if (input == 7)
                    {
                        try
                        {
                            _ = System.MemoryExtensions.AsSpan(source, -1);
                            return 0;
                        }
                        catch (System.ArgumentOutOfRangeException)
                        {
                            return 1;
                        }
                    }
                    return 0;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "StringAsSpanFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(466, ExecuteWithNode(result.ApplicationModule, assets.Directory, 1));
        Assert.Equal(366, ExecuteWithNode(result.ApplicationModule, assets.Directory, 2));
        Assert.Equal(267, ExecuteWithNode(result.ApplicationModule, assets.Directory, 3));
        Assert.Equal(267, ExecuteWithNode(result.ApplicationModule, assets.Directory, 4));
        Assert.Equal(267, ExecuteWithNode(result.ApplicationModule, assets.Directory, 5));
        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 6));
        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 7));
    }
}
