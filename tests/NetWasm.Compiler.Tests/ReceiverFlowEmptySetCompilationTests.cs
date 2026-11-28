// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A dispatched call no object reaches (a field nothing assigns) has no receivers to dispatch on, but still runs with a
// null receiver: it throws NullReferenceException, as on desktop, whatever the program constructs elsewhere. A generic
// virtual call, a virtual call and an interface call, each caught.
public sealed class ReceiverFlowEmptySetCompilationTests
{
    internal const string Source = """
        using System;

        public interface IShape { int Corners(); }
        public abstract class Shape : IShape
        {
            public abstract T Echo<T>(T value);
            public abstract int Area();
            public abstract int Corners();
        }
        public sealed class Square : Shape
        {
            public override T Echo<T>(T value) => value;
            public override int Area() => 4;
            public override int Corners() => 4;
        }
        public static class Unassigned
        {
            public static Shape? Shape;
            public static IShape? Interface;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                // A Square reaches calls of its own, so each declaration has a target elsewhere.
                var square = new Square();
                var total = square.Echo(input) + square.Area() + ((IShape)square).Corners();
                try { total += Unassigned.Shape!.Echo(input); } catch (NullReferenceException) { total += 100; }
                try { total += Unassigned.Shape!.Area(); } catch (NullReferenceException) { total += 1000; }
                try { total += Unassigned.Interface!.Corners(); } catch (NullReferenceException) { total += 10000; }
                return total;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ACallNoObjectReachesThrowsNullReferenceException(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowEmptySetFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        Assert.Equal(3 + 4 + 4 + 100 + 1000 + 10000, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 3, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
