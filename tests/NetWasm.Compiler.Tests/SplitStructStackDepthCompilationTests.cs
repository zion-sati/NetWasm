using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Struct scalar replacement pushes each field of a split local where the
// local pushed its whole value, so a store of a tuple local into an array
// element, with the array and index already on the stack, as PriorityQueue's
// sifts do, needs more stack than the body declared. Every preset compiles
// them and gives desktop .NET's results.
public sealed class SplitStructStackDepthCompilationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            // An array store of a struct local while the array and index are on the stack.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Sift((int Key, string Value)[] nodes, (int Key, string Value) node, int index)
            {
                while (index > 0)
                {
                    var parent = nodes[(index - 1) / 2];
                    if (node.Key >= parent.Key) break;
                    nodes[index] = parent;
                    index = (index - 1) / 2;
                }
                nodes[index] = node;
                return index;
            }

            public static int Run(int input)
            {
                var nodes = new (int Key, string Value)[16];
                var count = 0;
                var placed = 0;
                for (var i = 0; i < 12; i++)
                {
                    nodes[count] = default;
                    placed = placed * 3 + Sift(nodes, ((i * 7 + input) % 11, i.ToString()), count++);
                }
                var ints = new PriorityQueue<int, int>(Comparer<int>.Create((a, b) => b - a));
                var texts = new PriorityQueue<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < 20; i++)
                {
                    ints.Enqueue(i, (i * 7 + input) % 5);
                    texts.Enqueue(i.ToString(), ((i * 3 + input) % 4).ToString());
                }
                var drained = 0;
                while (ints.TryDequeue(out var element, out var priority)) drained = drained * 31 + element * 5 + priority;
                while (texts.TryDequeue(out var element, out _)) drained = drained * 7 + element.Length + element[0];
                return placed ^ drained ^ nodes[0].Key ^ nodes[0].Value.Length;
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, 440933858), (1, -1702748816), (2, 251678003), (5, -555653881), (9, 2123504171)];

    [Theory]
    [InlineData(FinalWasmOptimization.O0)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void SplitStructLocalsDeclareTheStackTheyNeed(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SplitStructStackDepth" + optimization, Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));
        foreach (var (input, expected) in Cases)
        {
            Assert.Equal(expected, ExecuteWithStandardWasiNode(
                result.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, result.StaticDataEnd));
        }
    }
}
