// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// List<T> and ReadOnlyCollection<T> throw through System.Collections.ThrowHelper.ThrowArgumentNullException with
// ExceptionArgument.collection and .list, which names its argument through GetArgumentName, a switch over every
// ExceptionArgument. A program reaches only the arguments its throws pass, so it carries only their names: the switch
// keeps the tests of values its callers pass (two here, so no single constant folds it), and each parameter's name still
// reaches its exception.
public sealed class ThrowHelperArgumentNamePayForUseCompilationTests
{
    // Names only GetArgumentName holds, none of which this program's throws pass.
    private static readonly string[] UnreachedNames = ["dictionary", "converter", "comparison"];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void AProgramCarriesOnlyTheArgumentNamesItsThrowsPass(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ThrowHelperArgumentNameFixture",
            """
            using System;
            using System.Collections.Generic;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = input;
                    try
                    {
                        _ = new List<int>(input > 100 ? new int[input] : null!);
                    }
                    catch (ArgumentNullException exception) when (exception.ParamName == "collection")
                    {
                        result += 10;
                    }
                    try
                    {
                        _ = new System.Collections.ObjectModel.ReadOnlyCollection<int>(input > 100 ? new int[input] : null!);
                    }
                    catch (ArgumentNullException exception) when (exception.ParamName == "list")
                    {
                        result += 100;
                    }
                    return result;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Equal(113, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target,
            result.StaticDataEnd));
        Assert.True(Holds(result.ApplicationModule, "collection"));
        Assert.True(Holds(result.ApplicationModule, "list"));
        Assert.All(UnreachedNames, name => Assert.False(Holds(result.ApplicationModule, name), name));
    }

    // A string literal is in the module's data as UTF-16 or UTF-8.
    private static bool Holds(byte[] module, string text) =>
        module.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0 ||
        module.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;
}
