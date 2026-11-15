// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class DevirtualizationCompilationTests
{
    private const string Source = """
        public class Shape
        {
            public virtual int Area(int scale) => scale;
            public virtual int Kind() => 1;
            public virtual int Visit<T>(T value) => value is int number ? number & 7 : -1;
        }
        public class Square : Shape
        {
            public override int Area(int scale) => scale * scale;
            // A new slot hides Shape.Kind; a call through Shape still reaches Shape.Kind.
            public new virtual int Kind() => 2;
            public override int Visit<T>(T value) => value is int number ? number & 3 : -2;
        }
        public interface IMeasure<T>
        {
            int Measure(T value) => value is null ? 0 : 10;
            int Twice(T value);
        }
        public sealed class Ruler : IMeasure<int>
        {
            public int Twice(int value) => value * 2;
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Shape shape = new Square();
                IMeasure<int> measure = new Ruler();
                var total = shape.Area(input) + shape.Kind() * 100 + shape.Visit(input) * 1000;
                total += measure.Measure(input) + measure.Twice(input) * 10000;
                // Reassigned on one path, so the call stays virtual and still dispatches.
                Shape either = new Shape();
                if (input > 2) either = new Square();
                total += either.Area(input) * 100000;
                return total;
            }
        }
        """;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void CallsOnAnAllocatedLocalReachTheImplementationDirectly(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("Devirtualization", Source) : assets.CompileSource("Devirtualization", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        var run = result.Program.Methods.Values.Single(body => body.Method.Definition.Name == "Run");
        var virtualCalls = run.Body.Instructions.Count(instruction => instruction.Operation == CilOperation.CallVirtual);

        // Only the reassigned local's call stays virtual once devirtualization runs.
        if (preset == FinalWasmOptimization.None)
        {
            Assert.True(virtualCalls > 1);
        }
        else
        {
            Assert.Equal(1, virtualCalls);
        }
        foreach (var input in new[] { 1, 5 })
        {
            Assert.Equal(Expected(input), ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static int Expected(int input)
    {
        var total = input * input + 1 * 100 + (input & 3) * 1000;
        total += 10 + input * 2 * 10000;
        return total + (input > 2 ? input * input : input) * 100000;
    }
}
