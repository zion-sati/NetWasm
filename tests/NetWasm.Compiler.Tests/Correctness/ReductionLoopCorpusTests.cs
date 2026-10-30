using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

// A loop that only folds array or span elements into locals with xor, or, and,
// a wrapping addition or an integer Math.Min or Math.Max folds each statement's
// range in bulk when every access is in bounds. Every integer element width and
// sign, offsets, several accumulators, truncating and widening accumulators,
// either operand order, elements copied to a local first (foreach) and every
// tail length match desktop, and so do the loops whose range leaves the array:
// the locals a catch sees and the exception. Floating sums, checked sums and a
// truncated minimum stay loops.
[Collection(CorrectnessTestGroup.Name)]
public sealed class ReductionLoopCorpusTests(CorrectnessTestRunner runner)
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.ReductionLoops;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var hash = 17;
                for (var size = 0; size <= 40; size++)
                {
                    hash = Mix(hash, input switch
                    {
                        0 => XorBytes(Bytes(size)),
                        1 => XorSignedBytes(SignedBytes(size)),
                        2 => XorShorts(Shorts(size)),
                        3 => OrChars(Chars(size)),
                        4 => AndInts(Ints(size)),
                        5 => (int)XorLongs(Longs(size)),
                        6 => XorByteSpan(Bytes(size)),
                        7 => CoveredIndex(Bytes(size + 200), 0, size + 200),
                        8 => OrIntSpan(Ints(size)),
                        9 => (int)WideningXor(Ints(size)),
                        10 => TwoAccumulators(Bytes(size), Shorts(size)),
                        11 => SameAccumulator(Ints(size), Ints(size + 3)),
                        12 => PastTheEnd(Bytes(size)),
                        13 => NullArray(size),
                        14 => Window(Bytes(size + 8), 3, size),
                        15 => AndSignedShortSpan(Shorts(size)),
                        16 => SumBytes(Bytes(size)),
                        17 => SumSignedBytes(SignedBytes(size)),
                        18 => SumShortSpan(Shorts(size)),
                        19 => SumChars(Chars(size)),
                        20 => SumInts(Ints(size)),
                        21 => (int)SumLongs(Longs(size)),
                        22 => (int)WideningSum(Ints(size)),
                        23 => (int)WideningUnsignedSum(UInts(size)),
                        24 => TruncatingSum(Bytes(size)),
                        25 => MinInts(Ints(size)),
                        26 => MaxSignedBytes(SignedBytes(size)),
                        27 => (int)MinUInts(UInts(size)),
                        28 => (int)MaxLongSpan(Longs(size)),
                        29 => (int)WideningMin(Ints(size)),
                        30 => (int)(MaxULongs(ULongs(size)) >> 13),
                        31 => LastCopy(Shorts(size)),
                        32 => SumPastTheEnd(Ints(size)),
                        33 => ValueFirst(Ints(size), Bytes(size)),
                        34 => TwoSums(Ints(size + 2), Bytes(size)),
                        35 => FloatSum(size),
                        36 => CheckedSum(Ints(size)),
                        _ => TruncatedMin(Ints(size)),
                    });
                }
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static byte XorBytes(byte[] data)
            {
                byte x = 0x5a;
                for (var i = 0; i < data.Length; i++) x ^= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int XorSignedBytes(sbyte[] data)
            {
                var x = 0;
                for (var i = 0; i < data.Length; i++) x ^= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static short XorShorts(short[] data)
            {
                short x = -2;
                for (var i = 0; i < data.Length; i++) x ^= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OrChars(char[] data)
            {
                var x = 0;
                for (var i = 0; i < data.Length; i++) x |= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AndInts(int[] data)
            {
                var x = -1;
                for (var i = 0; i < data.Length; i++) x &= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long XorLongs(long[] data)
            {
                var x = 0L;
                for (var i = 0; i < data.Length; i++) x ^= data[i];
                return x ^ (x >> 32);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static byte XorByteSpan(Span<byte> data)
            {
                byte x = 0;
                for (var i = 0; i < data.Length; i++) x ^= data[i];
                return x;
            }

            // Span.IndexerBench.CoveredIndex1's shape: offsets on both sides and an end that is an expression.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static byte CoveredIndex(Span<byte> data, int start, int end)
            {
                byte x = 0;
                byte y = 0;
                for (var idx = start; idx < end - 100; idx++)
                {
                    x ^= data[idx + 100];
                    y ^= data[idx];
                }
                for (var idx = end - 100; idx < end; idx++)
                {
                    y ^= data[idx];
                    x ^= data[idx - 100];
                }
                return (byte)(x * 7 + y);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OrIntSpan(ReadOnlySpan<int> data)
            {
                var x = 0;
                for (var i = 0; i < data.Length; i++) x |= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WideningXor(int[] data)
            {
                var x = 1L << 40;
                for (var i = 0; i < data.Length; i++) x ^= data[i];
                return x ^ (x >> 33);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoAccumulators(byte[] bytes, short[] shorts)
            {
                var x = 0;
                var y = 0;
                for (var i = 0; i < bytes.Length; i++)
                {
                    x ^= bytes[i];
                    y |= (ushort)shorts[i];
                }
                return x * 31 + y;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SameAccumulator(int[] first, int[] second)
            {
                var x = 7;
                for (var i = 0; i < first.Length; i++)
                {
                    x ^= first[i];
                    x ^= second[i + 3];
                }
                return x;
            }

            // The loop runs past the end: the fold up to the end, then the
            // exception at i == Length, as the loop alone gives them.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PastTheEnd(byte[] data)
            {
                var x = 0;
                var i = 0;
                var end = data.Length + 2;
                try
                {
                    for (i = 0; i < end; i++) x ^= data[i];
                }
                catch (IndexOutOfRangeException)
                {
                    return x * 1000 + i;
                }
                return -x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullArray(int size)
            {
                int[]? data = size % 4 == 0 ? null : Ints(size);
                var x = 3;
                var i = 0;
                try
                {
                    for (i = 0; i < size; i++) x ^= data![i];
                }
                catch (NullReferenceException)
                {
                    return x * 1000 + i + 7;
                }
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Window(byte[] data, int start, int count)
            {
                var x = 0;
                var end = start + count;
                for (var i = start; i < end; i++) x ^= data[i - 2];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static short AndSignedShortSpan(ReadOnlySpan<short> data)
            {
                short x = -1;
                for (var i = 0; i < data.Length; i++) x &= data[i];
                return x;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumBytes(byte[] data)
            {
                var sum = 0;
                for (var i = 0; i < data.Length; i++) sum += data[i];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumSignedBytes(sbyte[] data)
            {
                var sum = 0;
                foreach (var value in data) sum += value;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumShortSpan(ReadOnlySpan<short> data)
            {
                var sum = 0;
                foreach (var value in data) sum += value;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumChars(char[] data)
            {
                var sum = 0;
                for (var i = 0; i < data.Length; i++) sum += data[i];
                return sum;
            }

            // Elements reach 2^30 in magnitude, so the sum wraps.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumInts(int[] data)
            {
                var sum = 0;
                foreach (var value in data) sum += value;
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SumLongs(long[] data)
            {
                var sum = 0L;
                for (var i = 0; i < data.Length; i++) sum += data[i];
                return sum ^ (sum >> 32);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WideningSum(int[] data)
            {
                var sum = 1L << 40;
                for (var i = 0; i < data.Length; i++) sum += data[i];
                return sum ^ (sum >> 33);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WideningUnsignedSum(uint[] data)
            {
                var sum = -5L;
                foreach (var value in data) sum += value;
                return sum ^ (sum >> 31);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static byte TruncatingSum(byte[] data)
            {
                byte sum = 200;
                for (var i = 0; i < data.Length; i++) sum += data[i];
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MinInts(int[] data)
            {
                var min = int.MaxValue;
                foreach (var value in data) min = Math.Min(min, value);
                return min;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int MaxSignedBytes(sbyte[] data)
            {
                var max = -1000;
                for (var i = 0; i < data.Length; i++) max = Math.Max(max, data[i]);
                return max;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static uint MinUInts(uint[] data)
            {
                var min = uint.MaxValue - 3;
                for (var i = 0; i < data.Length; i++) min = Math.Min(min, data[i]);
                return min;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long MaxLongSpan(Span<long> data)
            {
                var max = long.MinValue + 9;
                for (var i = 0; i < data.Length; i++) max = Math.Max(data[i], max);
                return max ^ (max >> 32);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WideningMin(int[] data)
            {
                var min = 1L << 35;
                foreach (var value in data) min = Math.Min(min, value);
                return min ^ (min >> 32);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static ulong MaxULongs(ulong[] data)
            {
                var max = 3UL;
                for (var i = 0; i < data.Length; i++) max = Math.Max(max, data[i]);
                return max;
            }

            // The copy keeps the last element after the loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int LastCopy(short[] data)
            {
                var sum = 0;
                var max = short.MinValue;
                short last = -7;
                for (var i = 0; i < data.Length; i++)
                {
                    last = data[i];
                    sum += last;
                    max = Math.Max(max, last);
                }
                return sum * 31 + max * 7 + last;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SumPastTheEnd(int[] data)
            {
                var sum = 0;
                var i = 0;
                var end = data.Length + 1;
                try
                {
                    for (i = 0; i < end; i++) sum += data[i];
                }
                catch (IndexOutOfRangeException)
                {
                    return sum * 1000 + i;
                }
                return -sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ValueFirst(int[] ints, byte[] bytes)
            {
                var sum = 11;
                var min = 255;
                for (var i = 0; i < ints.Length; i++)
                {
                    sum = ints[i] + sum;
                    min = Math.Min(bytes[i], min);
                }
                return sum * 31 + min;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int TwoSums(int[] ints, byte[] bytes)
            {
                var sum = 0;
                for (var i = 0; i < bytes.Length; i++)
                {
                    sum += ints[i + 2];
                    sum += bytes[i];
                }
                return sum;
            }

            // Reordering a floating sum would change it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FloatSum(int size)
            {
                var data = new float[size];
                for (var index = 0; index < size; index++) data[index] = 1f / (index + 3) * (index % 2 == 0 ? 1e7f : 1f);
                var sum = 0f;
                for (var i = 0; i < data.Length; i++) sum += data[i];
                return BitConverter.SingleToInt32Bits(sum);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedSum(int[] data)
            {
                var sum = 0;
                try
                {
                    for (var i = 0; i < data.Length; i++) sum = checked(sum + data[i]);
                }
                catch (OverflowException)
                {
                    return sum - 1;
                }
                return sum;
            }

            // Truncating each minimum can reorder the elements, so this stays a loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static short TruncatedMin(int[] data)
            {
                short min = 0;
                for (var i = 0; i < data.Length; i++) min = (short)Math.Min(min, data[i]);
                return min;
            }

            private static byte[] Bytes(int size)
            {
                var data = new byte[size];
                for (var index = 0; index < size; index++) data[index] = (byte)(index * 37 + 11);
                return data;
            }

            private static sbyte[] SignedBytes(int size)
            {
                var data = new sbyte[size];
                for (var index = 0; index < size; index++) data[index] = (sbyte)(index * 53 - 90);
                return data;
            }

            private static short[] Shorts(int size)
            {
                var data = new short[size];
                for (var index = 0; index < size; index++) data[index] = (short)(index * 4099 - 30000);
                return data;
            }

            private static char[] Chars(int size)
            {
                var data = new char[size];
                for (var index = 0; index < size; index++) data[index] = (char)(1 << (index % 16));
                return data;
            }

            private static int[] Ints(int size)
            {
                var data = new int[size];
                for (var index = 0; index < size; index++) data[index] = ~(1 << (index % 31)) ^ (index << 20);
                return data;
            }

            private static uint[] UInts(int size)
            {
                var data = new uint[size];
                for (var index = 0; index < size; index++) data[index] = unchecked((uint)index * 2654435761u);
                return data;
            }

            private static ulong[] ULongs(int size)
            {
                var data = new ulong[size];
                for (var index = 0; index < size; index++) data[index] = unchecked((ulong)index * 0x9E3779B97F4A7C15UL);
                return data;
            }

            private static long[] Longs(int size)
            {
                var data = new long[size];
                for (var index = 0; index < size; index++) data[index] = unchecked(index * (long)0x9E3779B97F4A7C15UL);
                return data;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            public static int Trace() => 0;
        }
        """;

    [Fact]
    public void ReductionLoopsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "ReductionLoopCorpus",
            "NetWasm.Correctness.ReductionLoops",
            Source,
            [.. Enumerable.Range(0, 38)])
        {
            Optimization = FinalWasmOptimization.O3,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EveryFoldingLoopRunsInBulkAtO3(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ReductionLoops" + target, Source);

        var bulk = Compile(assets, assembly, target, null);
        var kept = Compile(assets, assembly, target,
            new WasmOptimizationOverrides(ArrayLoopIdioms: ManagedArrayLoopIdioms.Disabled));

        string[] loops =
        [
            "AndInts", "AndSignedShortSpan", "CoveredIndex", "LastCopy", "MaxLongSpan", "MaxSignedBytes", "MaxULongs",
            "MinInts", "MinUInts", "NullArray", "OrChars", "OrIntSpan", "PastTheEnd", "SameAccumulator", "SumBytes",
            "SumChars", "SumInts", "SumLongs", "SumPastTheEnd", "SumShortSpan", "SumSignedBytes", "TruncatingSum",
            "TwoAccumulators", "TwoSums", "ValueFirst", "WideningMin", "WideningSum", "WideningUnsignedSum", "WideningXor",
            "Window", "XorByteSpan", "XorBytes", "XorLongs", "XorShorts", "XorSignedBytes",
        ];
        Assert.Equal(string.Join(", ", loops), string.Join(", ", Folding(bulk)));
        Assert.Empty(Folding(kept));
        foreach (var input in (int[])[0, 7, 12, 13, 20, 26, 31, 32])
        {
            Assert.Equal(
                CompilerTestSupport.ExecuteWithStandardWasiNode(kept.ApplicationModule, assets.Directory, input, target, kept.StaticDataEnd),
                CompilerTestSupport.ExecuteWithStandardWasiNode(bulk.ApplicationModule, assets.Directory, input, target, bulk.StaticDataEnd));
        }
    }

    // The EntryPoint methods that call an ArrayLoops reduction.
    private static string[] Folding(CompilationResult result) =>
        [.. result.Program.Methods.Values
            .Where(static method => method.Method.DeclaringType.FullName == "NetWasm.Correctness.ReductionLoops.EntryPoint" &&
                method.Body.Instructions.Any(static instruction => instruction.Operand is CilOperand.MethodInstance
                {
                    Value:
                    {
                        DeclaringType.FullName: "System.Runtime.CompilerServices.ArrayLoops",
                        Definition.Name: "Xor32" or "Xor64" or "Or32" or "Or64" or "And32" or "And64" or "Sum32" or "Sum64" or
                            "Min32" or "Min64" or "Max32" or "Max64",
                    },
                }))
            .Select(static method => method.Method.Definition.Name)
            .Order(StringComparer.Ordinal)];

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmTarget target,
        WasmOptimizationOverrides? overrides) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Correctness.ReductionLoops.EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.O3, OptimizationOverrides: overrides));
}
