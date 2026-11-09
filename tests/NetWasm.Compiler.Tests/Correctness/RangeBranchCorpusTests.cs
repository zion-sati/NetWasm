using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A branch the ranges of its operands settle jumps or falls through, and the
// code it cuts off goes. Upstream's scalar fallback, whose `while (length >= 8)`
// never runs inside `if (length < 8)`, a loop test the counter's range settles,
// constants that meet at a join, stores that truncate to a byte, and span
// searches of every length match desktop on every profile and target. What the
// rule must leave, each against desktop with the rule on and off: counters that
// wrap past int.MaxValue, values a handler sees, unsigned tests, a local a
// callee changes through its address, a static field, and native ints.
public sealed class RangeBranchCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.RangeBranches;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var size = 0; size <= 21; size++)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => Fallback(Ints(size, 3), size / 2 * 3 * 7919 - 1000, size),
                        1 => Fallback(Ints(size, 3), -7, size),
                        2 => CounterRange(size),
                        3 => Joined(size, size % 3 == 0),
                        4 => Truncated(size * 37),
                        5 => Wrapping(size),
                        6 => Handled(size),
                        7 => UnsignedTest(size, 9),
                        8 => Chars(size).AsSpan().IndexOf((char)('a' + size / 2)),
                        9 => Chars(size).AsSpan().IndexOf('#'),
                        10 => Ints(size, 5).AsSpan().IndexOf(size / 3 * 5 * 7919 - 1000),
                        11 => Bytes(size).AsSpan().IndexOf((byte)(size / 2)),
                        12 => AddressTaken(size),
                        13 => FieldValue(size),
                        14 => NativeTest(size),
                        _ => throw new ArgumentOutOfRangeException(nameof(input)),
                    });
                }
                return hash;
            }

            // Upstream's NonPackedIndexOfValueType fallback, run below Vector128<int>.Count.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Fallback(int[] data, int value, int length)
            {
                if (length < 4)
                {
                    var offset = 0;
                    while (length >= 8)
                    {
                        length -= 8;
                        if (data[offset] == value) return offset;
                        if (data[offset + 1] == value) return offset + 1;
                        offset += 8;
                    }
                    return length > 0 && data[0] == value ? 0 : -1 - length;
                }
                return Search(data, value);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Search(int[] data, int value) => Array.IndexOf(data, value);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterRange(int limit)
            {
                var total = 0;
                for (var index = 0; index < 10; index++)
                {
                    if (index >= 10) return -1;
                    if (index > 20) total -= 1000;
                    if (index < limit) total += index;
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Joined(int value, bool first)
            {
                var chosen = first ? 5 : 9;
                if (chosen > 3) value += 100;
                if (chosen == 7) value -= 100;
                return chosen < 6 ? value : -value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Truncated(int value)
            {
                byte low = (byte)(value + 250);
                if (low > 255) return -1;
                if (low < 0) return -2;
                return low >= 128 ? low - 128 : low;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Wrapping(int steps)
            {
                var value = int.MaxValue - 4;
                for (var step = 0; step < steps; step++) value++;
                return value < 0 ? -1 : value - (int.MaxValue - 4);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Handled(int value)
            {
                var stage = 4;
                try
                {
                    stage = 0;
                    if (value % 4 == 1) throw new InvalidOperationException();
                    stage = 2;
                }
                catch (InvalidOperationException)
                {
                    if (stage == 0) return 1000 + value;
                }
                return stage == 2 ? value : -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int UnsignedTest(int value, int limit)
            {
                var shifted = value - 5;
                return (uint)shifted < (uint)limit ? shifted : -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddressTaken(int value)
            {
                var local = 5;
                Change(ref local, value);
                return local > 3 ? local : -local;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Change(ref int local, int value) => local -= value;

            private static int s_field = 2;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FieldValue(int value)
            {
                s_field = 2;
                Bump(value);
                return s_field > 2 ? s_field : -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(int value) => s_field += value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NativeTest(int value)
            {
                nint wide = value;
                nint limit = 8;
                if (value < 8)
                {
                    while (wide >= limit) wide -= 3;
                }
                return (int)wide;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static char[] Chars(int size)
            {
                var data = new char[size];
                for (var index = 0; index < size; index++) data[index] = (char)('a' + index);
                return data;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Ints(int size, int seed)
            {
                var data = new int[size];
                for (var index = 0; index < size; index++) data[index] = index * seed * 7919 - 1000;
                return data;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static byte[] Bytes(int size)
            {
                var data = new byte[size];
                for (var index = 0; index < size; index++) data[index] = (byte)index;
                return data;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void RangeBranchesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "RangeBranchCorpus",
            "NetWasm.Correctness.RangeBranches",
            Source,
            [.. Enumerable.Range(0, 15)])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    // The fallback's loop is gone at O3, and stays when the rule is off.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ALoopItsGuardRulesOutGoesAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("RangeBranches" + target, Source);

        var folded = Compile(assets, assembly, target, null);
        var kept = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(RangeBranchFolding: ManagedRangeBranchFolding.Disabled));

        Assert.Equal(0, BackEdges(folded, "Fallback"));
        Assert.Equal(1, BackEdges(kept, "Fallback"));
        foreach (var input in Enumerable.Range(0, 15))
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(kept.ApplicationModule, assets.Directory, input, target, kept.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(folded.ApplicationModule, assets.Directory, input, target, folded.StaticDataEnd));
        }
    }

    private static int BackEdges(CompilationResult result, string name) =>
        result.Program.Methods.Values
            .Single(method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.RangeBranches.EntryPoint" &&
                method.Method.Definition.Name == name)
            .Body.Instructions
            .Count(static instruction => instruction.Operand is CilOperand.BranchTarget { Offset: var target } &&
                target <= instruction.Offset && instruction.Operation != CilOperation.Leave);

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.RangeBranches.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
