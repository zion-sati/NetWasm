// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class StaticArrayPopulationCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void RemovesUnobservedPopulationWithoutRemovingAllocationsOrObservedValues(bool optimize, WasmTarget target)
    {
        const string source = """
            public readonly struct Pair
            {
                public readonly long X, Y;
                public Pair(long x, long y) { X = x; Y = y; }
            }
            public static class Data
            {
                private static readonly Pair[] Unused = [new(1, 2), new(3, 4)];
                public static readonly Pair[] Used = [new(5, 6)];
                public static readonly int Trigger = 7;
            }
            public static class EntryPoint
            {
                public static int Run(int input) => Data.Trigger + (int)Data.Used[0].X + (int)Data.Used[0].Y;
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("StaticPopulation", source) : assets.CompileSource("StaticPopulation", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        var initializer = Assert.Single(result.Program.Methods.Values, body =>
            body.Method.DeclaringType.FullName == "Data" && body.Method.Definition.Name == ".cctor");

        Assert.Equal(2, initializer.Body.Instructions.Count(instruction => instruction.Operation == CilOperation.NewArray));
        Assert.Equal(3, initializer.Body.Instructions.Count(instruction => instruction.Operation == CilOperation.StoreStaticField));
        Assert.Single(initializer.Body.Instructions, instruction => instruction.Operation == CilOperation.NewObject);
        Assert.Equal(18, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PreservesConstructorEffectsInitializationAndExceptions(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            public static class State { public static int Calls; }
            public struct Effect
            {
                public int Value;
                public Effect(int value) { State.Calls++; Value = value; }
            }
            public struct Initialized
            {
                public int Value;
                static Initialized() { State.Calls += 10; }
                public Initialized(int value) { Value = value; }
            }
            public struct Failing
            {
                public Failing(int value) { State.Calls += 100; throw new InvalidOperationException(); }
            }
            public static class Data
            {
                private static readonly Effect[] Effects = [new(1)];
                private static readonly Initialized[] Initializers = [new(2)];
                public static readonly int Trigger = 7;
            }
            public static class Bad
            {
                private static readonly Failing[] Values = [new(1)];
                public static readonly int Trigger = 9;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (Data.Trigger != 7 || State.Calls != 11) return -1;
                    try { _ = Bad.Trigger; return -2; }
                    catch (Exception) { return State.Calls == 111 ? 1 : -3; }
                }
            }
            """;
        AssertRuns(source, optimize, target);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PreservesInitializationOfAnotherClosedGenericOwner(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            public static class State { public static int Calls; }
            public readonly struct Value
            {
                public readonly int Number;
                public Value(int number) { Number = number; }
            }
            public static class Holder<T>
            {
                public static readonly Value Shared = new(3);
                private static readonly Value[] Unused = typeof(T) == typeof(int)
                    ? [Holder<string>.Shared] : [new(0)];
                public static readonly int Trigger = 7;
                static Holder() { State.Calls++; }
            }
            public static class EntryPoint
            {
                public static int Run(int input) => Holder<int>.Trigger == 7 && State.Calls == 2 ? 1 : -1;
            }
            """;
        AssertRuns(source, optimize, target);
    }

    private static void AssertRuns(string source, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("PopulationEffects", source) : assets.CompileSource("PopulationEffects", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        Assert.Equal(1, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }
}
