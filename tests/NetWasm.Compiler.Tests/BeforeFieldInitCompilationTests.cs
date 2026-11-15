// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class BeforeFieldInitCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void MethodCallsDeferRelaxedInitializationUntilTheExactOwnersFieldAccess(
        WasmTarget target, bool optimizedCil)
    {
        using var assets = TestAssets.Create();
        const string source = """
            namespace BeforeFieldInitFixture;
            public static class Probe
            {
                public static int Count;
                public static int Initialize() { Count++; return 11; }
            }
            public static class Direct
            {
                public static int Value = Probe.Initialize();
                public static int Read() => 7;
            }
            public static class Closed<T>
            {
                public static int Value = Probe.Initialize();
                public static int Read() => 7;
            }
            public static class Strict
            {
                static Strict() { Probe.Count++; }
                public static int Read() => 7;
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Probe.Count = 0;
                    if (Direct.Read() + Closed<int>.Read() + Closed<string>.Read() != 21 || Probe.Count != 0) return -1;
                    if (Strict.Read() != 7 || Probe.Count != 1) return -2;
                    if (Direct.Value != 11 || Probe.Count != 2) return -3;
                    Closed<int>.Value = 19;
                    if (Closed<int>.Value != 19 || Probe.Count != 3) return -4;
                    ref var value = ref Closed<string>.Value;
                    if (value != 11 || Probe.Count != 4) return -5;
                    if (Direct.Read() + Closed<int>.Read() + Closed<string>.Read() + Strict.Read() != 28 || Probe.Count != 4) return -6;
                    return 42;
                }
            }
            """;
        var assembly = optimizedCil
            ? assets.CompileOptimizedSource("BeforeFieldInitFixture", source)
            : assets.CompileSource("BeforeFieldInitFixture", source);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "BeforeFieldInitFixture.EntryPoint", "Run", [], Target: target));

        // The profile deliberately chooses the latest legal BeforeFieldInit
        // trigger; a desktop oracle may legally initialize these owners earlier.
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
    }
}
