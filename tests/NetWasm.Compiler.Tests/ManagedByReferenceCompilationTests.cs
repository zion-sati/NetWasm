// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ManagedByReferenceCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3DoesNotTreatConditionalRefReturnsAsExactAliases(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "ConditionalRefReturnAggregateFixture",
            """
            using System.Runtime.CompilerServices;

            public struct Buffer
            {
                public int Value;
            }

            public static class EntryPoint
            {
                private static int s_external = 7;

                public static int Run(int input) =>
                    Probe(new Buffer { Value = input }, input != 0);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(Buffer value, bool chooseValue)
                {
                    ref var selected = ref Choose(ref value.Value, chooseValue);
                    var before = selected;
                    value.Value += 100;
                    return before * 1000 + value.Value;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static ref int Choose(ref int value, bool chooseValue)
                {
                    if (chooseValue)
                    {
                        return ref value;
                    }
                    return ref s_external;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Target: target,
            Optimization: FinalWasmOptimization.O3));

        Assert.NotEmpty(result.ApplicationModule);
        Assert.Equal(target, result.Layouts.Target.Target);
        Assert.Equal(5105, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 5, target, result.StaticDataEnd));
        Assert.Equal(7100, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3SynchronizesPromotedAggregateAcrossWholeLocalTransfers(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "AddressTakenAggregateWholeLocalFixture",
            """
            public struct Buffer
            {
                public int Low;
                public int High;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = Create(input);
                    value.Low += 2;
                    var observed = Read(value);
                    return observed + value.Low + value.High + value.Low;
                }

                [System.Runtime.CompilerServices.MethodImpl(
                    System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static Buffer Create(int input) => new()
                {
                    Low = input,
                    High = 3,
                };

                [System.Runtime.CompilerServices.MethodImpl(
                    System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static int Read(Buffer value) =>
                    value.Low * 100 + value.High;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Target: target,
            Optimization: FinalWasmOptimization.O3));

        Assert.NotEmpty(result.ApplicationModule);
        Assert.Equal(target, result.Layouts.Target.Target);
        Assert.Equal(4290, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 40, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3PromotesReferenceFreeByRefAggregateWithCallerVisibleWrites(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "ManagedByRefAggregatePromotionFixture",
            """
            public struct Buffer
            {
                public int Low;
                public long High;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new Buffer { Low = input, High = 5 };
                    Mutate(ref value, input);
                    return value.Low * 100 + (int)value.High;
                }

                private static void Mutate(ref Buffer value, int count)
                {
                    for (var index = 0; index < count; index++)
                    {
                        value.Low += index + 1;
                        value.High += value.Low;
                    }
                    value.Low++;
                    value.High += value.Low;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Target: target,
            Optimization: FinalWasmOptimization.O3));

        Assert.NotEmpty(result.ApplicationModule);
        Assert.Equal(target, result.Layouts.Target.Target);
        Assert.Equal(1034, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 3, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void O3PromotesAddressTakenScalarAcrossReadAndWriteCalls(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(
            "AddressTakenScalarPromotionFixture",
            """
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    var before = Read(ref value);
                    Increment(ref value);
                    return before * 100 + value;
                }

                private static int Read(ref int value) => value;

                private static void Increment(ref int value) => value += 2;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            Target: target,
            Optimization: FinalWasmOptimization.O3));

        Assert.NotEmpty(result.ApplicationModule);
        Assert.Equal(target, result.Layouts.Target.Target);
        Assert.Equal(4143, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void CompilerPreservesScalarReferenceLocalsArgumentsAndReturns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ManagedByReferenceFixture",
            """
            namespace ManagedByReferenceFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var left = input;
                    var right = 1;
                    ref var selected = ref Select(ref left, ref right, input != 0);
                    Increment(ref selected);
                    return left * 10 + right;
                }

                private static ref int Select(ref int left, ref int right, bool first)
                {
                    if (first)
                    {
                        return ref left;
                    }
                    return ref right;
                }

                private static void Increment(ref int value) => value++;
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ManagedByReferenceFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(421, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        Assert.Equal(2, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
        Assert.DoesNotContain(result.Program.ConstructedTypes, type =>
            type.Shape is CliTypeShape.ManagedByReference or
                CliTypeShape.UnmanagedPointer);
        Assert.False(result.Program.RequiresTypeFacts);
        Assert.Empty(result.Program.MethodDescriptors);
        Assert.Empty(result.Program.FieldDescriptors);
    }

    [Fact]
    public void CompilerEmitsManagedReferencesForBothAddressWidths()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ManagedByReferenceWidthFixture",
            """
            namespace ManagedByReferenceWidthFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = input;
                    return Read(ref value);
                }

                private static int Read(ref int value) => value;
            }
            """);

        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ManagedByReferenceWidthFixture.EntryPoint",
                "Run",
                [],
                target));

            Assert.NotEmpty(result.ApplicationModule);
            Assert.Equal(target, result.Layouts.Target.Target);
        }
    }

    [Fact]
    public void CompilerPreservesFieldArrayAndReadonlyReferences()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ManagedInteriorReferenceFixture",
            """
            namespace ManagedInteriorReferenceFixture;

            public sealed class Holder
            {
                public int Value;
            }

            public static class EntryPoint
            {
                public static int Field(int input)
                {
                    var holder = new Holder { Value = input };
                    ref var field = ref holder.Value;
                    field++;
                    return field;
                }

                public static int Array(int input)
                {
                    var values = new int[] { 1, input };
                    ref var element = ref values[1];
                    element++;
                    return element;
                }

                public static int Readonly(int input)
                {
                    var values = new int[] { input };
                    ref readonly var readonlyElement = ref values[0];
                    return readonlyElement;
                }

                public static int Run(int input)
                {
                    var holder = new Holder { Value = input };
                    ref var field = ref holder.Value;
                    var values = new int[] { 1, 2 };
                    ref var element = ref values[1];
                    field++;
                    element += field;
                    ref readonly var readonlyElement = ref values[0];
                    return field * 100 + element * 10 + readonlyElement;
                }
            }
            """);

        Assert.Equal(42, CompileAndRun("Field"));
        Assert.Equal(42, CompileAndRun("Array"));
        Assert.Equal(41, CompileAndRun("Readonly"));
        Assert.Equal(4641, CompileAndRun("Run"));

        int CompileAndRun(string method)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ManagedInteriorReferenceFixture.EntryPoint",
                method,
                []));
            return ExecuteWithNode(result.ApplicationModule, assets.Directory, 41);
        }
    }
}
