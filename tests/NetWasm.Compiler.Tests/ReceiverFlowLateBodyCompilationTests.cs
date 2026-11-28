// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// The receiver flow can reach a method before the closure has its body: an expression tree names Where, Select and Sum
// by member descriptor, which the flow takes as boundaries at once, while the closure compiles their bodies only once
// it plans their execution. Each body joins the flow when it arrives: Select's call of an iterator's own Select (which
// makes a where-select iterator) must run in the flow, or that iterator never reaches Sum's foreach, whose Dispose then
// has no case for it.
public sealed class ReceiverFlowLateBodyCompilationTests
{
    internal const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;

        namespace ReceiverFlowLateBodyFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Expression<Func<List<int>, int, int>> sum = (list, k) => list.Where(i => i % k == 0).Select(i => i * k).Sum();
                return sum.Compile()(new List<int> { 1, 2, 3, 4, 5, input }, 2);
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void MethodsReachedBeforeTheirBodiesJoinTheFlowWhenTheBodiesArrive(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ReceiverFlowLateBodyFixture", Source, TestAssets.RepositorySdkVersion, "latest", optimize: true);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "ReceiverFlowLateBodyFixture.EntryPoint", "Run", [], Optimization: optimization));

        // The even numbers 2, 4 and 6, each doubled.
        Assert.Equal(24, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 6, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }
}
