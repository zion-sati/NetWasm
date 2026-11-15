// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericReferenceComparisonCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerInvokesARelaxedReferenceMethodGroupDelegate(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string librarySource = """
            using System;

            namespace GenericReferenceComparisonLibrary;

            public abstract class Item;

            public sealed class FirstItem : Item;

            public static class Workload
            {
                public static int Run()
                {
                    Item first = new FirstItem();
                    Item second = first;
                    Func<Item, Item, bool> compare = object.ReferenceEquals;
                    return compare(second, first) ? 3 : 0;
                }
            }
            """;
        const string applicationSource = """
            using GenericReferenceComparisonLibrary;

            namespace GenericReferenceComparisonApplication;

            public static class EntryPoint
            {
                public static int Run(int input) => Workload.Run();
            }
            """;
        var library = optimize
            ? assets.CompileOptimizedSource("GenericReferenceComparisonLibrary", librarySource)
            : assets.CompileSource("GenericReferenceComparisonLibrary", librarySource);
        var assembly = optimize
            ? assets.CompileOptimizedSource(
                "GenericReferenceComparisonApplication",
                applicationSource,
                library)
            : assets.CompileSource(
                "GenericReferenceComparisonApplication",
                applicationSource,
                library);
        var compilation = NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [assets.CoreLib, library],
                "GenericReferenceComparisonApplication.EntryPoint",
                "Run",
                [],
                target));
        var observed = CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd);

        Assert.Equal(3, observed);
    }
}
