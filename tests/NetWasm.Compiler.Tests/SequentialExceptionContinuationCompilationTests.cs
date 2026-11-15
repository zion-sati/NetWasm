// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class SequentialExceptionContinuationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitsSequentialCatchContinuationsOnce(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SequentialExceptionContinuations", """
            using System;
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                public static int Run(int unused)
                {
                    try { Fail(0); return 1; }
                    catch (IndexOutOfRangeException) { if (unused < 0) return 3; }
                    if (unused == 0)
                    {
                        try { Fail(1); return 2; }
                        catch (NullReferenceException) { }
                    }
                    try { Fail(1); return 4; }
                    catch (NullReferenceException) { }
                    return 42;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Fail(int mode)
                {
                    if (mode == 0) throw new IndexOutOfRangeException();
                    throw new NullReferenceException();
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 0, target, compilation.StaticDataEnd));
        Assert.Equal(42, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, 1, target, compilation.StaticDataEnd));
        Assert.Equal(3, ExecuteWithStandardWasiNode(compilation.ApplicationModule,
            assets.Directory, -1, target, compilation.StaticDataEnd));
    }
}
