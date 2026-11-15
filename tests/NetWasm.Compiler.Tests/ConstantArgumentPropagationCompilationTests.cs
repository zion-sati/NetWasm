// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ConstantArgumentPropagationCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void RuntimeIntrinsicPlaceholdersAreNotConstantProofs(bool optimize, WasmTarget target)
    {
        const string source = """
            using System.Runtime.CompilerServices;
            public struct HasReference { public object? Value; }
            public static class EntryPoint
            {
                public static int Inspect(int marker) => marker +
                    (RuntimeHelpers.IsReferenceOrContainsReferences<HasReference>() ? 100 : 0);
                public static int Run(int input) => Inspect(1);
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("IntrinsicConstantBoundary", source) : assets.CompileSource("IntrinsicConstantBoundary", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        Assert.Equal(101, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DefaultProviderFormattingDoesNotRetainTheGeneralFormatter(bool optimize, WasmTarget target)
    {
        const string source = """
            using System.Globalization;
            public static class EntryPoint
            {
                public static int Run(int input) => input.ToString(CultureInfo.InvariantCulture) ==
                    (input < 0 ? "-42" : "42") ? 1 : 0;
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("DefaultProviderRetention", source) : assets.CompileSource("DefaultProviderRetention", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values);

        Assert.DoesNotContain(methods, body => body.Method.DeclaringType.FullName == "System.Number" &&
            (body.Method.Definition.Name is "NumberToString" or "NumberToStringFormat" or "ParseFormatSpecifier" ||
             body.Method.Definition.Name.Contains("FormatInt32Slow", StringComparison.Ordinal)));
        Assert.Equal(1, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd));
        Assert.Equal(1, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, -42, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PrunesKnownArgumentBranchThroughAnOrdinaryUserPredicate(bool optimize, WasmTarget target)
    {
        const string source = """
            public static class EntryPoint
            {
                public static int NeverCalled() => -100;
                public static bool IsDefault(object? value) => value == null;
                public static int Select(int input, int unused, object? format, object? provider)
                {
                    if (IsDefault(format)) return input;
                    return NeverCalled();
                }
                public static int Run(int input) => Select(input, 0, null, null);
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("ConstantArguments", source) : assets.CompileSource("ConstantArguments", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));

        Assert.DoesNotContain(result.Program.Methods.Values, body => body.Method.Definition.Name == "NeverCalled");
        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void PreservesUnknownCallersAliasingInitializationAndNoInlining(bool optimize, WasmTarget target)
    {
        const string source = """
            using System;
            using System.Runtime.CompilerServices;
            public static class State { public static int Calls; }
            public static class Initialized
            {
                static Initialized() { State.Calls++; }
                public static bool Predicate(object? value) => value == null;
            }
            public static class EntryPoint
            {
                public static int Escape(int value) => value + 1;
                public static int Select(object? value) => value == null ? 3 : 5;
                public static void Mutate(ref int value) { value = 9; }
                public static int AddressTaken(int value) { Mutate(ref value); return value; }
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static bool KeepCall(object? value) => value == null;
                public static int Run(int input)
                {
                    Func<int, int> callback = Escape;
                    var result = Escape(7) + callback(input) + Select(null) + Select("x") + AddressTaken(7);
                    if (!Initialized.Predicate(null) || State.Calls != 1) return -1;
                    if (!KeepCall(null)) return -2;
                    return result;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("ConstantArgumentBoundaries", source) : assets.CompileSource("ConstantArgumentBoundaries", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));

        Assert.Contains(result.Program.Methods.Values, body => body.Method.Definition.Name == "KeepCall");
        Assert.Equal(68, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 42, target, result.StaticDataEnd));
        Assert.Equal(29, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, target, result.StaticDataEnd));
    }
}
