// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConstantArgumentExceptionCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesLiveAggregateStorageWhenExceptionRegionIsPruned(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = Assert.IsAssignableFrom<ICompilationScenarioExecutor>(new CompilationScenarioExecutor(assets));
        var result = executor.Execute(new CompilationScenario(
            "PrunedStorageExceptionRegion",
            """
            using System;

            public static class EntryPoint
            {
                private sealed class Box { public int Value; }
                private struct Holder { public Box Value; }

                public static int Run(int input)
                {
                    var holder = new Holder { Value = new Box { Value = 42 } };
                    return Choose(false, ref holder) + input;
                }

                private static int Choose(bool enter, ref Holder holder)
                {
                    if (enter)
                    {
                        try { return 13; }
                        catch (Exception) { return 17; }
                    }
                    GC.Collect();
                    return holder.Value.Value;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            Input: 3,
            ReferencePaths: []));

        Assert.Equal(45, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesPrunedThrowInsideReachableProtectedRegion(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = Assert.IsAssignableFrom<ICompilationScenarioExecutor>(new CompilationScenarioExecutor(assets));
        var result = executor.Execute(new CompilationScenario(
            "PrunedThrow",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input) => Choose(false) + input;

                private static int Choose(bool enter)
                {
                    try
                    {
                        if (enter)
                            throw new Exception();
                    }
                    catch (Exception)
                    {
                        return 17;
                    }
                    return 42;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            Input: 3,
            ReferencePaths: []));

        Assert.Equal(45, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerRemovesExceptionRegionsInPrunedBranches(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = Assert.IsAssignableFrom<ICompilationScenarioExecutor>(new CompilationScenarioExecutor(assets));
        var result = executor.Execute(new CompilationScenario(
            "PrunedExceptionRegion",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input) => Choose(false) + input;

                private static int Choose(bool enter)
                {
                    if (enter)
                    {
                        try
                        {
                            return 13;
                        }
                        catch (Exception)
                        {
                            return 17;
                        }
                    }
                    return 42;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            Input: 3,
            ReferencePaths: []));

        Assert.Equal(45, result);
    }
}
