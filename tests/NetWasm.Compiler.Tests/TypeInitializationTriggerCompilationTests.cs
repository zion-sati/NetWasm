// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class TypeInitializationTriggerCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ExpressionInterfaceInvocationInitializesItsValueTarget(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Linq.Expressions;
            public static class State { public static int Count; }
            public interface IRead { int Read(); }
            public struct ValueOwner : IRead
            {
                static ValueOwner() { State.Count++; }
                public int Read() => 41 + State.Count;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Expression<Func<IRead, int>> expression = value => value.Read();
                    var read = expression.Compile();
                    var result = read(default(ValueOwner));
                    return result == 42 && State.Count == 1 ? 42 : -1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("DescriptorInitializationTriggers", source)
            : assets.CompileSource("DescriptorInitializationTriggers", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, 0)]
    [InlineData(false, WasmTarget.Wasm32, 1)]
    [InlineData(false, WasmTarget.Wasm64, 0)]
    [InlineData(false, WasmTarget.Wasm64, 1)]
    [InlineData(true, WasmTarget.Wasm32, 0)]
    [InlineData(true, WasmTarget.Wasm32, 1)]
    [InlineData(true, WasmTarget.Wasm64, 0)]
    [InlineData(true, WasmTarget.Wasm64, 1)]
    public void DelegateInvocationInitializesItsSelectedTarget(bool optimize, WasmTarget target, int input)
    {
        const string source = """
            using System;
            public static class State { public static int Count; }
            public static class StaticOwner<T>
            {
                static StaticOwner() { State.Count++; }
                public static int Read() => 41 + State.Count;
            }
            public struct ValueOwner<T>
            {
                public int Number;
                static ValueOwner() { State.Count++; }
                public int Read() => Number + State.Count;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Func<int> read;
                    if (input == 0)
                        read = StaticOwner<int>.Read;
                    else
                    {
                        ValueOwner<int> value = default;
                        value.Number = 41;
                        read = value.Read;
                    }
                    var first = read();
                    return first == 42 && read() == 42 && State.Count == 1 ? 42 : -1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("DelegateInitializationTriggers", source)
            : assets.CompileSource("DelegateInitializationTriggers", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OptimizationsPreserveInitializationWhenRemovingCalls(bool optimize, WasmTarget target)
    {
        const string source = """
            using System.Runtime.CompilerServices;
            public static class State { public static int Count; }
            public static class StaticOwner
            {
                static StaticOwner() { State.Count++; }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static int Read(int value) => value;
            }
            public struct ValueOwner
            {
                public int Number;
                static ValueOwner() { State.Count++; }
                public int Value => Number;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    ValueOwner value = default;
                    value.Number = input;
                    var result = StaticOwner.Read(value.Value);
                    _ = StaticOwner.Read(value.Value);
                    return result + State.Count;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("OptimizedInitializationTriggers", source)
            : assets.CompileSource("OptimizedInitializationTriggers", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O3));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 40, target, result.StaticDataEnd));
    }

    public static TheoryData<int, bool, WasmTarget> Cells
    {
        get
        {
            var cells = new TheoryData<int, bool, WasmTarget>();
            for (var scenario = 0; scenario < 8; scenario++)
                foreach (var optimize in new[] { false, true })
                    foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                        cells.Add(scenario, optimize, target);
            return cells;
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void MethodInvocationHonorsExplicitInitializationTriggers(
        int scenario, bool optimize, WasmTarget target)
    {
        const string source = """
            using System.Runtime.CompilerServices;
            public static class State { public static int Count; }
            public interface IRead { int Read(); }
            public sealed class ReferenceOwner<T>
            {
                public int Value;
                static ReferenceOwner() { State.Count++; }
                [MethodImpl(MethodImplOptions.NoInlining)]
                public ReferenceOwner(int value) { Value = value; }
            }
            public struct ValueOwner<T> : IRead
            {
                public int Value;
                static ValueOwner() { State.Count++; }
                [MethodImpl(MethodImplOptions.NoInlining)]
                public ValueOwner(int value) { Value = value; }
                [MethodImpl(MethodImplOptions.NoInlining)]
                public int Read() => Value + State.Count;
            }
            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReadConstrained<T>(ref T value) where T : struct, IRead
                    => value.Read();
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReadBoxed(IRead value) => value.Read();
                public static int Run(int input)
                {
                    State.Count = 0;
                    switch (input)
                    {
                        case 0:
                            var reference = new ReferenceOwner<int>(41);
                            return reference.Value + State.Count;
                        case 1:
                            var value = new ValueOwner<int>(41);
                            return value.Value + State.Count;
                        case 2:
                            var array = new ValueOwner<int>[] { new(41) };
                            return array[0].Value + State.Count;
                        case 3:
                            var first = new ReferenceOwner<int>(20);
                            var second = new ReferenceOwner<string>(20);
                            _ = new ReferenceOwner<int>(0);
                            return first.Value + second.Value + State.Count;
                        case 4:
                            ValueOwner<int> direct = default;
                            direct.Value = 41;
                            return direct.Read();
                        case 5:
                            ValueOwner<int> constrained = default;
                            constrained.Value = 41;
                            return ReadConstrained(ref constrained);
                        case 6:
                            ValueOwner<int> boxed = default;
                            boxed.Value = 41;
                            return ReadBoxed(boxed);
                        default:
                            ValueOwner<int> untouched = default;
                            untouched.Value = 42;
                            return State.Count == 0 ? untouched.Value : -1;
                    }
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("InitializationTriggers", source)
            : assets.CompileSource("InitializationTriggers", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, scenario, target, result.StaticDataEnd));
    }
}
