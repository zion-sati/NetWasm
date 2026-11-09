using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A loop that reads Unsafe.Add of a reference and its counter reads a reference
// that moves with the counter instead. Upstream's SequenceCompareTo vector loop,
// a native int counter read as it is, an int counter read as an int, sign- or
// zero-extended (the span indexer's `(nint)(uint)index`), counters that count
// down, start below the reference or step by two, two references, a counter
// read after the loop leaves early, and span reads past the end or before the
// start, which throw where desktop's do, match desktop on every profile and
// target; loops that call a method or allocate, and byte reads, keep their reads.
public sealed class AddressInductionCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Runtime.Intrinsics;

        namespace NetWasm.Correctness.AddressInduction;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var size = 0; size <= 21; size++)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => CompareChars(Chars(size + 1, -1), Chars(size + 1, size / 2)),
                        1 => SumWithChoice(Ints(size, 3)),
                        2 => SpanChoice(Ints(size, 5)),
                        3 => CountDown(Ints(size, 7)),
                        4 => BelowTheReference(Ints(size + 6, 9), size),
                        5 => NativeBelowTheReference(Ints(size + 6, 11), size),
                        6 => TwoReferences(Ints(size, 13), size),
                        7 => StepByTwo(Ints(size, 15)),
                        8 => CounterAfterTheLoop(Ints(size, 17), size * 1000),
                        9 => SpanPastTheEnd(Ints(size, 19)),
                        10 => SpanBeforeTheStart(Ints(size, 21), size % 3 - 1),
                        11 => CallInTheLoop(Ints(size, 23)),
                        12 => AllocationInTheLoop(Ints(size, 25)),
                        _ => XorBytes(Bytes(size)),
                    });
                }
                return hash;
            }

            // Upstream's SequenceCompareTo(ref char, int, ref char, int).
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CompareChars(char[] first, char[] second)
            {
                ref char a = ref MemoryMarshal.GetArrayDataReference(first);
                ref char b = ref MemoryMarshal.GetArrayDataReference(second);
                nuint length = (nuint)Math.Min(first.Length, second.Length);
                nuint i = 0;
                if (length >= (nuint)Vector128<ushort>.Count)
                {
                    nuint last = length - (nuint)Vector128<ushort>.Count;
                    do
                    {
                        if (Vector128.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.Add(ref a, (nint)i))) !=
                            Vector128.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.Add(ref b, (nint)i))))
                        {
                            break;
                        }
                        i += (nuint)Vector128<ushort>.Count;
                    }
                    while (last >= i);
                }
                while (i < length)
                {
                    var result = Unsafe.Add(ref a, (nint)i).CompareTo(Unsafe.Add(ref b, (nint)i));
                    if (result != 0) return result * 1000 + (int)i;
                    i += 1;
                }
                return first.Length - second.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumWithChoice(int[] values)
            {
                ref int start = ref MemoryMarshal.GetArrayDataReference(values);
                var total = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    var value = Unsafe.Add(ref start, i);
                    total = value > 0 ? total * 3 + value : total - value * 5;
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SpanChoice(int[] values)
            {
                Span<int> span = values;
                var total = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    var value = span[i];
                    total = value > 100 ? total * 3 + value : total - value;
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CountDown(int[] values)
            {
                ref int start = ref MemoryMarshal.GetArrayDataReference(values);
                var total = 0;
                for (var i = values.Length - 1; i >= 0; i--) total = total * 3 + Unsafe.Add(ref start, i);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BelowTheReference(int[] values, int size)
            {
                ref int middle = ref values[3];
                var total = 0;
                for (var i = -3; i < size + 3; i++) total = total * 7 + Unsafe.Add(ref middle, i) * (i + 5);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NativeBelowTheReference(int[] values, int size)
            {
                ref int middle = ref values[3];
                var total = 0;
                for (var i = -3; i < size + 3; i++) total = total * 7 + Unsafe.Add(ref middle, (nint)i) - i;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoReferences(int[] source, int size)
            {
                var target = new int[size];
                ref int from = ref MemoryMarshal.GetArrayDataReference(source);
                ref int to = ref MemoryMarshal.GetArrayDataReference(target);
                for (nint i = 0; i < size; i++)
                {
                    var value = Unsafe.Add(ref from, i);
                    Unsafe.Add(ref to, i) = value > 0 ? value * 2 + Unsafe.Add(ref from, i) : -value;
                }
                var total = 0;
                foreach (var value in target) total = total * 31 + value;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StepByTwo(int[] values)
            {
                ref int start = ref MemoryMarshal.GetArrayDataReference(values);
                var total = 0;
                for (var i = 0; i + 1 < values.Length; i += 2) total = total * 5 + Unsafe.Add(ref start, i) - Unsafe.Add(ref start, i + 1);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterAfterTheLoop(int[] values, int limit)
            {
                ref int start = ref MemoryMarshal.GetArrayDataReference(values);
                var i = 0;
                for (; i < values.Length; i++)
                {
                    if (Unsafe.Add(ref start, i) > limit) break;
                }
                return i * 100 + values.Length;
            }

            // The loops that throw have no handler of their own, which would keep their reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SpanPastTheEnd(int[] values)
            {
                var progress = new int[1];
                try
                {
                    return SpanPastTheEndLoop(values, progress);
                }
                catch (IndexOutOfRangeException)
                {
                    return progress[0] + 1;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SpanPastTheEndLoop(int[] values, int[] progress)
            {
                Span<int> span = values;
                ref int total = ref progress[0];
                for (var i = 0; i <= span.Length; i++) total = total * 3 + (span[i] > 0 ? span[i] : 1);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SpanBeforeTheStart(int[] values, int first)
            {
                var progress = new int[1];
                try
                {
                    return SpanBeforeTheStartLoop(values, first, progress);
                }
                catch (IndexOutOfRangeException)
                {
                    return progress[0] - 1;
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SpanBeforeTheStartLoop(int[] values, int first, int[] progress)
            {
                ReadOnlySpan<int> span = values;
                ref int total = ref progress[0];
                for (var i = first; i < span.Length; i++) total = total * 3 + (span[i] > 0 ? span[i] : 2);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CallInTheLoop(int[] values)
            {
                ref int start = ref MemoryMarshal.GetArrayDataReference(values);
                var total = 0;
                for (var i = 0; i < values.Length; i++) total = Combine(total, Unsafe.Add(ref start, i));
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Combine(int total, int value) => total * 3 + value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AllocationInTheLoop(int[] values)
            {
                ref int start = ref MemoryMarshal.GetArrayDataReference(values);
                var total = 0;
                for (var i = 0; i < values.Length; i++)
                {
                    var box = (object)Unsafe.Add(ref start, i);
                    total = total * 3 + (int)box;
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static char[] Chars(int size, int mismatch)
            {
                var data = new char[size];
                for (var index = 0; index < size; index++) data[index] = (char)('a' + index % 23);
                if (mismatch >= 0 && mismatch < size) data[mismatch] = 'Z';
                return data;
            }

            // Span.IndexerBench.Indexer5: a byte's address costs what p's advance would, so the read stays.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int XorBytes(byte[] values)
            {
                var span = values.AsSpan();
                byte x = 0;
                var last = -1;
                for (var index = 0; index < span.Length; index++)
                {
                    x ^= span[index];
                    last = index;
                }
                return x * 1000 + last;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static byte[] Bytes(int size)
            {
                var data = new byte[size];
                for (var index = 0; index < size; index++) data[index] = (byte)(index * 37 + 5);
                return data;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Ints(int size, int seed)
            {
                var data = new int[size];
                for (var index = 0; index < size; index++) data[index] = index * seed * 7919 - 1000;
                return data;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void AddressInductionLoopsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "AddressInductionCorpus",
            "NetWasm.Correctness.AddressInduction",
            Source,
            [.. Enumerable.Range(0, 14)])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ReadsOfTheCounterMoveWithItAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("AddressInduction" + target, Source);

        var reduced = Compile(assets, assembly, target, null);
        var indexed = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(LoopStrengthReduction: ManagedLoopStrengthReduction.Disabled));

        string[] loops =
        [
            "BelowTheReference", "CompareChars", "CountDown", "CounterAfterTheLoop", "NativeBelowTheReference",
            "SpanBeforeTheStartLoop", "SpanChoice", "SpanPastTheEndLoop", "StepByTwo", "SumWithChoice", "TwoReferences",
        ];
        var actual = string.Join(", ", Advanced(reduced));
        Assert.True(string.Join(", ", loops) == actual, "advanced: " + actual);
        Assert.Empty(Advanced(indexed));
        foreach (var input in Enumerable.Range(0, 14))
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(indexed.ApplicationModule, assets.Directory, input, target, indexed.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(reduced.ApplicationModule, assets.Directory, input, target, reduced.StaticDataEnd));
        }
    }

    // The EntryPoint methods that move a reference local by a constant, `p = ref Unsafe.Add(ref p, c)`.
    private static string[] Advanced(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.AddressInduction.EntryPoint" &&
                Advances(method.Body.Instructions))
            .Select(static method => method.Method.Definition.Name)
            .Order(StringComparer.Ordinal)];

    private static bool Advances(System.Collections.Immutable.ImmutableArray<CilInstruction> instructions)
    {
        for (var index = 2; index + 1 < instructions.Length; index++)
        {
            if (instructions[index] is { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value.Definition.Name: "Add" } } &&
                instructions[index + 1] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } &&
                instructions[index - 1].Operation is CilOperation.LoadInt32 or CilOperation.ConvertNativeInt &&
                (instructions[index - 1].Operation == CilOperation.LoadInt32 ? index - 2 : index - 3) is var load && load >= 0 &&
                instructions[load] is { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var loaded } } &&
                loaded == stored)
            {
                return true;
            }
        }
        return false;
    }

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.AddressInduction.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
