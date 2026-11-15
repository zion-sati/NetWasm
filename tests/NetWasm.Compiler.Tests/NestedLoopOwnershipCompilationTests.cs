// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NestedLoopOwnershipCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesNestedLoopBranchesInsideEnumeratorCleanup(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Collections.Generic;

            namespace NestedLoopOwnershipFixture;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    if (Match([1, 2, 3], [3, 2, 1]) != 3)
                    {
                        return 1;
                    }

                    if (Match([1, 2], [1, 3]) != -1)
                    {
                        return 2;
                    }

                    return value;
                }

                private static int Match(
                    IList<int> expected,
                    IEnumerable<int> actual)
                {
                    var used = new bool[expected.Count];
                    var matches = 0;
                    using var enumerator = actual.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var current = enumerator.Current;
                        for (var index = 0; index < expected.Count; index++)
                        {
                            if (used[index])
                            {
                                continue;
                            }

                            if (current == expected[index])
                            {
                                used[index] = true;
                                matches++;
                                break;
                            }

                            if (current < expected[index] ||
                                index == expected.Count - 1)
                            {
                                return -1;
                            }
                        }
                    }

                    return matches;
                }
            }
            """;
        ICompilationScenarioExecutor executor =
            new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "NestedLoopOwnershipOptimized" : "NestedLoopOwnershipDebug",
            source,
            "NestedLoopOwnershipFixture.EntryPoint",
            optimize,
            target,
            17,
            []));

        Assert.Equal(17, observed);
    }
}
