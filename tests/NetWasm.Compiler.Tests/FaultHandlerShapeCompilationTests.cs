// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// Fault handlers (C# writes none; the fixture's finally regions are rewritten to faults) in the shapes that move blocks
// around a handler: span copies inlined into the fault, whose range checks share one throw that never returns (the
// shape behind the endfinally placement, here ending in endfault), and a loop whose break, return and throw leave the
// protected block. A fault runs only when its block is left by an exception, so each expected value differs from what a
// finally would give.
public sealed class FaultHandlerShapeCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;

        namespace FaultShapes;

        public sealed class ShapeException(int code) : Exception("shape " + code) { public int Code { get; } = code; }

        public static class Shapes
        {
            public static int Marks;

            public static int Restore(byte[] data, int input)
            {
                Span<byte> saved = stackalloc byte[16];
                data.AsSpan(4, 8).CopyTo(saved);
                try
                {
                    data.AsSpan(4, 8).Clear();
                    if (input % 2 == 1) throw new ShapeException(input);
                    return data[4] + input;
                }
                finally
                {
                    saved.Slice(0, 4).CopyTo(data.AsSpan(4));
                    saved.Slice(4, 4).CopyTo(data.AsSpan(8));
                }
            }

            public static int Loop(IList<int> list, int input)
            {
                var total = 7;
                try
                {
                    for (var index = 0; index < list.Count; index++)
                    {
                        var value = list[index];
                        if (value == input) break;
                        if (value == input + 3) return total * 2;
                        if (value == input + 5) throw new ShapeException(value);
                        total += value;
                    }
                }
                finally
                {
                    Marks++;
                }
                return total + 1000;
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (input >= 100)
                {
                    var data = new byte[32];
                    for (var index = 0; index < data.Length; index++) data[index] = (byte)(index + 1);
                    try { return Shapes.Restore(data, input - 100) * 1000 + data[4] + data[8]; }
                    catch (ShapeException exception) { return -(exception.Code * 1000 + data[4] + data[8]); }
                }
                Shapes.Marks = 0;
                var list = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
                try { return Shapes.Loop(list, input - 50) * 10 + Shapes.Marks; }
                catch (ShapeException exception) { return -(exception.Code * 10 + Shapes.Marks); }
            }
        }
        """;

    public static TheoryData<FinalWasmOptimization, WasmTarget> Cases()
    {
        var cases = new TheoryData<FinalWasmOptimization, WasmTarget>();
        foreach (var optimization in (FinalWasmOptimization[])[FinalWasmOptimization.O0, FinalWasmOptimization.O1,
                     FinalWasmOptimization.O3, FinalWasmOptimization.Oz])
        {
            cases.Add(optimization, WasmTarget.Wasm32);
            cases.Add(optimization, WasmTarget.Wasm64);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FaultHandlersRunOnlyOnExceptionsWhateverTheirShape(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler("FaultShapes", Source, TestAssets.RepositorySdkVersion, "latest",
            optimize: true);
        MethodBodyPatcher.RewriteExceptionRegionKind(assembly, "FaultShapes.Shapes", "Restore",
            CilExceptionRegionKind.Finally, CilExceptionRegionKind.Fault);
        MethodBodyPatcher.RewriteExceptionRegionKind(assembly, "FaultShapes.Shapes", "Loop",
            CilExceptionRegionKind.Finally, CilExceptionRegionKind.Fault);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "FaultShapes.EntryPoint",
            "Run", [], Optimization: optimization)
        { Target = target });

        // Restore: the fault restores the cleared bytes only when the copy's block throws.
        Assert.Equal(6000, Run(106));
        Assert.Equal(-7014, Run(107));
        // Loop: break, return and a completed loop leave without the fault; a throw runs it once.
        Assert.Equal(10130, Run(54));
        Assert.Equal(140, Run(48));
        Assert.Equal(-11, Run(46));
        Assert.Equal(10850, Run(70));

        int Run(int input) => ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target,
            result.StaticDataEnd);
    }
}
