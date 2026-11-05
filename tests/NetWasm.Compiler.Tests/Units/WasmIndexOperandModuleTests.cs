using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Units;

// Compiled-unit relocation decodes every function body the compiler emits; an
// identity rewrite must reproduce each body and a shifted one must move only indices.
public sealed class WasmIndexOperandModuleTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new List<int>();
                for (var i = 0; i < input + 8; i++) values.Add((i * 37) % 11);
                values.Sort();
                var text = string.Join(",", values);
                var vector = Vector128.Create(input, 2, 3, 4) * Vector128.Create(5);
                try
                {
                    if (text.Length > 1000) throw new InvalidOperationException(text);
                    return checked(text.Length + vector.GetElement(0) + Dictionary(input));
                }
                catch (OverflowException)
                {
                    return -1;
                }
                finally
                {
                    values.Clear();
                }
            }

            private static int Dictionary(int input)
            {
                var map = new Dictionary<string, decimal> { ["a"] = 1.5m, ["b"] = input };
                var sum = 0m;
                foreach (var value in map.Values) sum += value;
                return (int)sum + (map.TryGetValue("c", out var c) ? (int)c : 0);
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O0, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O0, WasmTarget.Wasm64)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    public void EveryEmittedBodyRewritesExactlyAndShiftsOnlyItsIndices(
        FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UnitRelocation", Source);
        var module = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            Optimization: optimization)).ApplicationModule;
        var scanner = new WasmIndexOperandScanner();
        var rewriter = new WasmIndexOperandRewriter();
        var bodies = CodeSectionBodies(module);
        var calls = 0;

        Assert.NotEmpty(bodies);
        foreach (var body in bodies)
        {
            var operands = scanner.Scan(body);
            Assert.Equal(body, rewriter.Rewrite(body, operands, new Shift(0)));
            var shifted = rewriter.Rewrite(body, operands, new Shift(1_000));
            var rescanned = scanner.Scan(shifted);
            Assert.Equal(operands.Select(operand => (operand.Space, operand.Index + 1_000)),
                rescanned.Select(operand => (operand.Space, operand.Index)));
            calls += operands.Count(operand => operand.Space == WasmIndexSpace.Function);
        }
        Assert.True(calls > bodies.Count, $"only {calls} calls in {bodies.Count} bodies");
    }

    // Recording each method's queries for its compiled unit must not change what it emits.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RecordingUnitDependenciesLeavesTheModuleUnchanged(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UnitRecording", Source);
        var options = new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O0);

        var plain = NetWasmCompiler.Compile(options).ApplicationModule;
        var recorded = NetWasmCompiler.Compile(options with
        {
            IntermediateOutputPath = Path.Combine(assets.Directory, "obj"),
            CompiledUnits = true,
        }).ApplicationModule;

        Assert.Equal(plain, recorded);
    }

    private static List<byte[]> CodeSectionBodies(byte[] module)
    {
        var position = 8;
        var bodies = new List<byte[]>();
        while (position < module.Length)
        {
            var id = module[position++];
            var size = (int)Unsigned(module, ref position);
            var end = position + size;
            if (id == 10)
            {
                var count = Unsigned(module, ref position);
                for (var body = 0u; body < count; body++)
                {
                    var length = (int)Unsigned(module, ref position);
                    bodies.Add(module[position..(position + length)]);
                    position += length;
                }
            }
            position = end;
        }
        return bodies;
    }

    private static uint Unsigned(byte[] bytes, ref int position)
    {
        uint value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var next = bytes[position++];
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0) return value;
        }
    }

    private sealed class Shift(uint by) : IWasmIndexRemapping
    {
        public uint Remap(WasmIndexSpace space, uint index) => index + by;
    }
}
