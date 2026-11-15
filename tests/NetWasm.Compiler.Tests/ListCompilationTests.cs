// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ListCompilationTests
{
    [Fact]
    public void CompilerLowersGenericListGrowthIndexingAndForeach()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ListFixture",
            """
            using System.Collections.Generic;

            namespace ListFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var values = new List<int>();
                    values.Add(input);
                    values.Add(1);
                    values.Add(100);
                    values[2] = 0;
                    var total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }
                    values.Clear();
                    return total + values.Count;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ListFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
