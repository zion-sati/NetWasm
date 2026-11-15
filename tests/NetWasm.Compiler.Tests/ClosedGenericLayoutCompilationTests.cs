// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ClosedGenericLayoutCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OpenGenericTypeMetadataCoexistsWithClosedValueStorage(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = """
            using System;
            namespace GenericValueMetadata;
            public struct Cell<T> { public T Value; }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var definition = typeof(Cell<>);
                    if (!definition.IsValueType || !definition.IsGenericTypeDefinition ||
                        !definition.ContainsGenericParameters) return 1;
                    if (!typeof(Cell<int>).IsGenericType) return 2;
                    object number = new Cell<int> { Value = input };
                    object text = new Cell<string> { Value = "retained" };
                    if (((Cell<int>)number).Value != input || ((Cell<string>)text).Value != "retained") return 3;
                    if (typeof(Cell<int>).ContainsGenericParameters ||
                        typeof(Cell<string>).IsGenericTypeDefinition) return 4;
                    return 42;
                }
            }
            """;
        var assembly = optimized ? assets.CompileOptimizedSource("GenericValueMetadata", source) :
            assets.CompileSource("GenericValueMetadata", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "GenericValueMetadata.EntryPoint", "Run", [], Target: target));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            41, target, compilation.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, false, WasmTarget.Wasm32)]
    [InlineData(false, false, WasmTarget.Wasm64)]
    [InlineData(false, true, WasmTarget.Wasm32)]
    [InlineData(false, true, WasmTarget.Wasm64)]
    [InlineData(true, false, WasmTarget.Wasm32)]
    [InlineData(true, false, WasmTarget.Wasm64)]
    [InlineData(true, true, WasmTarget.Wasm32)]
    [InlineData(true, true, WasmTarget.Wasm64)]
    public void NullableDelegateArgumentsRetainTheirClosedLayout(
        bool expression, bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var create = expression
            ? "Expression<Func<int?, bool>> tree = value => value == input; var predicate = tree.Compile();"
            : "Func<int?, bool> predicate = value => value == input;";
        var source = $$"""
            using System;
            using System.Linq.Expressions;
            namespace ClosedNullableDelegate;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    {{create}}
                    if (!predicate(input) || predicate(input + 1) || predicate(null)) return 1;
                    return 42;
                }
            }
            """;
        var assembly = optimized ? assets.CompileOptimizedSource("ClosedNullableDelegate", source) :
            assets.CompileSource("ClosedNullableDelegate", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "ClosedNullableDelegate.EntryPoint", "Run", [], Target: target));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory,
            41, target, compilation.StaticDataEnd));
    }
}
