using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A span over RVA data lowers to the data field's address, typed as the field's
// blob, so a byref local given it meets its paths that hold the element type,
// or nothing yet, at a jump past its scope, a loop entry or a handler. Neither
// pointee holds references, so the merge roots nothing through the pointer;
// every preset compiles them, survives collections between, and gives desktop
// .NET's results, a stack value holding a reference reinterpreted in a loop too.
public sealed class TableAddressMergeCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public static class EntryPoint
        {
            private struct Holder { public string Text; public int Value; }
            private static long s_long = 0x2122232425262728;

            private static ReadOnlySpan<sbyte> Map => [-1, 3, 5, -7, 9, 11, -13, 15, 17, -19, 21, 23, -25, 27, 29, -31];

            // A jump past the byref local's scope meets the path that assigned it a table's address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SkippedScope(int input)
            {
                var total = input;
                if (input > 4) goto Done;
                {
                    ref sbyte map = ref MemoryMarshal.GetReference(Map);
                    for (var i = 0; i < 16; i++) total += Unsafe.Add(ref map, i) * (i + 1);
                }
            Done:
                GC.Collect();
                return total;
            }

            // Each iteration assigns the local again, so the loop entry meets the back edge.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AssignedInLoop(int input)
            {
                var total = 0;
                for (var round = 0; round < input % 4 + 1; round++)
                {
                    ref sbyte map = ref MemoryMarshal.GetReference(Map);
                    total = total * 3 + Unsafe.Add(ref map, (round + input) & 15);
                    ref byte bytes = ref Unsafe.As<long, byte>(ref s_long);
                    total += Unsafe.Add(ref bytes, round & 7);
                    GC.Collect();
                }
                return total;
            }

            // A stack value holding a reference, reinterpreted inside a loop; its string must survive.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReferenceHolderInLoop(int input)
            {
                var holder = new Holder { Text = new string('x', input + 1), Value = input * 13 };
                var total = 0;
                for (var round = 0; round < 3; round++)
                {
                    ref byte view = ref Unsafe.As<Holder, byte>(ref holder);
                    GC.Collect();
                    total += Unsafe.As<byte, Holder>(ref view).Value + Unsafe.As<byte, Holder>(ref view).Text.Length;
                }
                return total;
            }

            // An exception handler sees the local both unassigned and assigned.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int HandlerMerge(int input)
            {
                var total = 0;
                try
                {
                    if (input == 3) throw new InvalidOperationException();
                    ref sbyte map = ref MemoryMarshal.GetReference(Map);
                    total = Unsafe.Add(ref map, input & 15);
                    if (input == 5) throw new InvalidOperationException();
                }
                catch (InvalidOperationException)
                {
                    GC.Collect();
                    total -= 100;
                }
                return total;
            }

            public static int Run(int input) =>
                SkippedScope(input) + AssignedInLoop(input) * 7 + ReferenceHolderInLoop(input) * 31 + HandlerMerge(input) * 131;
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, 829), (1, 3594), (2, 7784), (3, 1954), (4, 7421), (5, -3798), (9, 10192)];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O0)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O1)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O2)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Os)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void TableAddressesMeetTheirLocalsOtherPaths(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"TableAddressMerge_{target}_{optimization}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));
        foreach (var (input, expected) in Cases)
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
