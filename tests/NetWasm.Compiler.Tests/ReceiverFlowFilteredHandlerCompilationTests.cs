// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A filtered catch handler runs after its filter accepts, but the graph has no edge from endfilter to the handler, so
// the receiver flow must give the handler what reaches the filter and what the filter stores. Here the code after the
// handler is reached only through it: a virtual call on a local stored before the try, and one on the catch variable
// Roslyn stores in the filter, each with two possible receivers. A non-virtual instance call on the local too, which
// has one target whatever reaches it.
public sealed class ReceiverFlowFilteredHandlerCompilationTests
{
    internal const string Source = """
        using System;
        using System.Collections.Generic;

        public abstract class Shape { public abstract int Area(); }
        public sealed class Square : Shape { public override int Area() => 4; }
        public sealed class Circle : Shape { public override int Area() => 3; }
        public class Fault : Exception { public virtual int Code() => 1; }
        public sealed class BigFault : Fault { public override int Code() => 10; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Shape shape = input == 0 ? new Square() : new Circle();
                var counts = new List<int> { 7, input };
                var total = 0;
                try
                {
                    Fail(input);
                    return -1;
                }
                catch (Fault fault) when (fault.Code() > 0)
                {
                    total += fault.Code() * 100;
                }
                return total + shape.Area() + counts[0] * 10000;
            }

            private static void Fail(int input) => throw (input == 0 ? new BigFault() : new Fault());
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, 0, 71004)]
    [InlineData(FinalWasmOptimization.None, 1, 70103)]
    [InlineData(FinalWasmOptimization.O1, 0, 71004)]
    [InlineData(FinalWasmOptimization.O1, 1, 70103)]
    [InlineData(FinalWasmOptimization.O3, 0, 71004)]
    [InlineData(FinalWasmOptimization.Oz, 1, 70103)]
    public void CallsAfterAFilteredHandlerTakeWhatReachedTheFilter(FinalWasmOptimization optimization, int input,
        int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowFilteredHandlerFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        Assert.Equal(expected, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
