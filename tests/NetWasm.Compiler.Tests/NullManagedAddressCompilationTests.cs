// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A null managed address follows Mono AOT's rule at every optimization level: an indirect access (ldind/stind,
// ldobj/stobj, Volatile and Interlocked, Unsafe.Add then a read) throws NullReferenceException exactly when the address
// it computes is 0, so null plus a non-zero offset reads memory; a field access through a managed address throws when
// its base is 0. O0's per-access checks define the behavior, and every level must throw exactly where O0 does, at the
// same access: each scenario reports a bit when it throws NullReferenceException (anything else fails the run), and a
// cursor walking down to address 0 reports how many reads it made first. The shapes cover what removes or moves checks:
// proofs, loop-entry checks, vectorized and idiom-recognized loops.
public sealed class NullManagedAddressCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading;

        public struct Pair
        {
            public int A;
            public int B;
        }

        public enum Small : byte { A = 1, B = 2 }

        public static class Accesses
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Read(ref int value) => value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Sum(ReadOnlySpan<int> values, int from)
            {
                var total = 0;
                for (var index = from; index < values.Length; index++) total += values[index];
                return total;
            }

            // Loops whose body reads through the argument, offset or moved as a cursor: the shapes a loop-entry check
            // would guard.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Masked(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++) total += Unsafe.Add(ref first, index & 3);
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Walked(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    total += first;
                    first = ref Unsafe.Add(ref first, 1);
                }
                return total;
            }

            public static int DownProgress;

            // From a small non-zero address down to 0: four reads, the last at 0.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Down(ref int start, int count)
            {
                var total = 0;
                ref var cursor = ref start;
                for (var index = 0; index < count; index++)
                {
                    total += cursor;
                    DownProgress++;
                    cursor = ref Unsafe.Subtract(ref cursor, 1);
                }
                return total;
            }

            // A loop whose first access is one element past the argument: from a null argument no access is at 0.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int OffsetFirst(ref int first, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++) total += Unsafe.Add(ref first, 1 + (index & 3));
                return total;
            }

            // A read one element in, then a read of the argument itself: from a null argument the second is at 0.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int ReadAfterOffset(ref int first) => Unsafe.Add(ref first, 1) + first;

            // An offset from a local's address that lands on address 0: an offset proves nothing about the address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static unsafe int LocalOffsetToZero()
            {
                var local = 7;
                return Unsafe.Add(ref Unsafe.As<int, byte>(ref local), -(nint)Unsafe.AsPointer(ref local));
            }

            // An unaligned access, which is not checked, then a plain one through the same address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int UnalignedThenRead(ref byte address) => Unsafe.ReadUnaligned<int>(ref address) + address;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int UnalignedWriteThenRead(ref byte address)
            {
                Unsafe.WriteUnaligned(ref address, 5);
                return address;
            }

            // An elementwise loop the vectorizer takes: not a copy or a fill.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Scale(Span<int> destination, ReadOnlySpan<int> source)
            {
                for (var index = 0; index < source.Length; index++) destination[index] = source[index] * 3 + 1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void ScaleFrom(Span<int> destination, ReadOnlySpan<int> source, int from)
            {
                for (var index = from; index < source.Length; index++) destination[index] = source[index] * 3 + 1;
            }

            // Folds the elementwise vectorizer takes, over a span from an index and over a walked reference.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int FoldShifted(ReadOnlySpan<int> values, int from)
            {
                var total = 0;
                for (var index = from; index < values.Length; index++) total ^= values[index] << 1;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int WalkFold(ref int start, int count)
            {
                var total = 0;
                ref var cursor = ref start;
                var remaining = count;
                do
                {
                    total += cursor;
                    cursor = ref Unsafe.Add(ref cursor, 1);
                    remaining--;
                }
                while (remaining != 0);
                return total;
            }

            // Plain span loops, which a test ahead of the loop versions: a copy without checks runs where no element the
            // loop reads is at address 0, and the loop itself elsewhere.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Hash(ReadOnlySpan<char> text)
            {
                var hash = 17;
                for (var index = 0; index < text.Length; index++) hash = hash * 31 + text[index];
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int HashFrom(ReadOnlySpan<int> values, int from)
            {
                var hash = 17;
                for (var index = from; index < values.Length; index++) hash = hash * 31 + values[index];
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Prefix(Span<int> values)
            {
                var sum = 0;
                for (var index = 0; index < values.Length; index++)
                {
                    sum += values[index];
                    values[index] = sum;
                }
                return sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Find(ReadOnlySpan<int> values, int target)
            {
                for (var index = 0; index < values.Length; index++) if (values[index] == target) return index;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Cursor(ReadOnlySpan<int> values)
            {
                ref var start = ref MemoryMarshal.GetReference(values);
                var total = 0;
                for (nint index = 0; index < values.Length; index++) total ^= Unsafe.Add(ref start, index) * 3;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int PairFields(ReadOnlySpan<Pair> pairs)
            {
                var total = 0;
                foreach (ref readonly var pair in pairs) total += pair.A * pair.B;
                return total;
            }

            // Reads its counter after the loop, so a copy of the loop may not count with a counter of its own.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int CounterAfter(ReadOnlySpan<int> values)
            {
                var total = 0;
                int index;
                for (index = 0; index < values.Length; index++) total = total * 31 + values[index];
                return total + index;
            }

            // Reads at one index only, so no read goes near the top of the address space: from a reference four elements
            // below address 0, the read at index 4 is at 0, though the first element is not.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Gather(ref int first, int count, int at)
            {
                var total = 0;
                for (var index = 0; index < count; index++) if (index == at) total += Unsafe.Add(ref first, index);
                return total;
            }

            // The same, with a bound the loop raises through a reference to it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Raise(ref int first, int count, int at)
            {
                ref var limit = ref count;
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    if (index == 0) limit = 8;
                    if (index == at) total += Unsafe.Add(ref first, index);
                }
                return total;
            }

            // Reads one element behind and one ahead of its counter, from index 1.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Peak(ReadOnlySpan<int> values)
            {
                for (var index = 1; index < values.Length - 1; index++)
                {
                    if (values[index - 1] < values[index] && values[index] > values[index + 1]) return index;
                }
                return -1;
            }

            // A reference that is a local's on one path and null on the other.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Choose(bool flag)
            {
                var local = 5;
                ref var chosen = ref flag ? ref local : ref Unsafe.NullRef<int>();
                return chosen;
            }

            // A counter that starts at one of two values, which join where it is stored: a call cannot become a select.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Two() => 2;

            // A trip count no level can fold, so O3 cannot unroll the loop before it reaches the versioner.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Four() => 4;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int TernaryStart(ReadOnlySpan<int> values, bool flag)
            {
                var total = 0;
                for (var index = flag ? 0 : Two(); index < values.Length; index++) total = total * 31 + values[index];
                return total;
            }

            public static int Touched;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Touch() => Touched++;

            // A copy of the counter stored in one arm only, so the read after the arm sees the value from before the
            // loop on the other paths: from a reference a hundred elements below address 0, the first read is at 0.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Arm(ref int first)
            {
                var total = 0;
                var at = 100;
                var count = Four();
                for (var index = 0; index < count; index++)
                {
                    if (index == 3)
                    {
                        Touch();
                        at = index + 1;
                    }
                    total += Unsafe.Add(ref first, at);
                }
                return total;
            }

            // Reads one element ahead of its counter.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Ahead(ReadOnlySpan<int> values)
            {
                var total = 0;
                for (var index = 0; index < values.Length - 1; index++) total += values[index + 1] - values[index];
                return total;
            }

            // A span based two elements below address 0: element 2 is at 0, and elements 0 and 1 are at the top of the
            // address space, which a loop from index 2 never reads.
            public static ReadOnlySpan<int> BelowZero(int length) =>
                MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Subtract(ref Unsafe.NullRef<int>(), 2), length);

            // Span loops whose reads lie at an offset from the counter by values the loop leaves alone, which bounds
            // versioning proves in range: a three-tap stencil at start + index - 1 to start + index + 1, a difference
            // with the row above at start + index - width, and a lookup at an index read from memory, which only its own
            // check bounds. A test of the span's whole run picks a copy without their address checks.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Stencil(ReadOnlySpan<int> source, int start, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    var at = start + index;
                    total += source[at - 1] + source[at] + source[at + 1];
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Above(ReadOnlySpan<int> pixels, int start, int count, int width)
            {
                var total = 0;
                for (var index = 0; index < count; index++)
                {
                    var at = start + index;
                    total += pixels[at] - pixels[at - width];
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Lookup(ReadOnlySpan<int> keys, ReadOnlySpan<int> table, int count)
            {
                var total = 0;
                for (var index = 0; index < count; index++) total ^= table[keys[index] & 15] + index;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int SumAll(ReadOnlySpan<int> values)
            {
                var total = 0;
                foreach (var value in values) total += value;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Fill(Span<byte> values, byte value)
            {
                for (var index = 0; index < values.Length; index++) values[index] = value;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Copy(Span<int> destination, ReadOnlySpan<int> source)
            {
                for (var index = 0; index < source.Length; index++) destination[index] = source[index];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Fields(ref Pair pair, int value)
            {
                pair.B = value;
                return pair.A;
            }
        }

        public static class EntryPoint
        {
            private static int s_sink;

            private static int Scenario(int scenario, int input)
            {
                switch (scenario)
                {
                    case 0: return Accesses.Read(ref Unsafe.NullRef<int>());
                    case 1: return Unsafe.Add(ref Unsafe.NullRef<int>(), 3);
                    case 2: Unsafe.Add(ref Unsafe.NullRef<int>(), 2) = input; return 0;
                    case 3: return Accesses.Fields(ref Unsafe.NullRef<Pair>(), input);
                    case 4: return Accesses.Sum(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 16), 0);
                    case 5: return Accesses.Sum(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 16), 3);
                    case 6: return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<byte>(), 256).IndexOf((byte)0xA5);
                    case 7: return Volatile.Read(ref Unsafe.NullRef<int>());
                    case 8: return Interlocked.Increment(ref Unsafe.NullRef<int>());
                    case 9: return (int)Interlocked.Exchange(ref Unsafe.NullRef<long>(), input);
                    case 10: return Interlocked.CompareExchange(ref Unsafe.NullRef<object?>(), null, null) is null ? 0 : 1;
                    case 11: return Interlocked.CompareExchange<string?>(ref Unsafe.NullRef<string?>(), null, null) is null ? 0 : 1;
                    case 12: return (int)Interlocked.Exchange(ref Unsafe.NullRef<Small>(), Small.B);
                    case 13: return Accesses.Masked(ref Unsafe.NullRef<int>(), 6);
                    case 14: return Accesses.Walked(ref Unsafe.NullRef<int>(), 4);
                    case 15: return Accesses.Down(ref Unsafe.Add(ref Unsafe.NullRef<int>(), 3), 6);
                    case 16: return Accesses.SumAll(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 64));
                    case 17: Accesses.Fill(MemoryMarshal.CreateSpan(ref Unsafe.NullRef<byte>(), 64), (byte)input); return 0;
                    case 18:
                        Accesses.Copy(MemoryMarshal.CreateSpan(ref Unsafe.NullRef<int>(), 8), new int[8]);
                        return 0;
                    case 19: return Accesses.OffsetFirst(ref Unsafe.NullRef<int>(), 6);
                    case 20: return Accesses.ReadAfterOffset(ref Unsafe.NullRef<int>());
                    case 21:
                        Accesses.Scale(new int[16], MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 16));
                        return 0;
                    case 22:
                        Accesses.Scale(MemoryMarshal.CreateSpan(ref Unsafe.NullRef<int>(), 16), new int[16]);
                        return 0;
                    case 23: return (int)Unsafe.ReadUnaligned<ulong>(ref Unsafe.NullRef<byte>());
                    case 24: Unsafe.WriteUnaligned(ref Unsafe.NullRef<byte>(), (ushort)input); return 0;
                    case 25:
                        return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<char>(), 3000)
                            .SequenceCompareTo(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<char>(), 3000));
                    case 26: return Accesses.LocalOffsetToZero();
                    case 27: return Accesses.UnalignedThenRead(ref Unsafe.NullRef<byte>());
                    case 28: return Accesses.UnalignedWriteThenRead(ref Unsafe.NullRef<byte>());
                    case 29: return Accesses.Sum(Accesses.BelowZero(64), 2);
                    case 30:
                        Accesses.ScaleFrom(new int[64], Accesses.BelowZero(64), 2);
                        return 0;
                    case 31: return Accesses.FoldShifted(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 64), 0);
                    case 32: return Accesses.FoldShifted(Accesses.BelowZero(64), 2);
                    case 33: return Accesses.WalkFold(ref Unsafe.NullRef<int>(), 64);
                    case 34: return Accesses.Hash(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<char>(), 64));
                    case 35: return Accesses.HashFrom(Accesses.BelowZero(64), 2);
                    case 36: return Accesses.Prefix(MemoryMarshal.CreateSpan(ref Unsafe.NullRef<int>(), 64));
                    case 37: return Accesses.Find(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 64), -5);
                    case 38: return Accesses.Cursor(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 64));
                    case 39: return Accesses.PairFields(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<Pair>(), 64));
                    case 40:
                        // Element 1 of a span based one element below address 0 is at 0, and the loop reads it first.
                        return Accesses.Ahead(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Subtract(ref Unsafe.NullRef<int>(), 1), 64));
                    case 41: return Accesses.Gather(ref Unsafe.Subtract(ref Unsafe.NullRef<int>(), 4), 8, 4);
                    case 42: return Accesses.Raise(ref Unsafe.Subtract(ref Unsafe.NullRef<int>(), 4), 2, 4);
                    case 43: return Accesses.Peak(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 64));
                    case 44: return Accesses.Choose(false);
                    case 45: return Accesses.Arm(ref Unsafe.Subtract(ref Unsafe.NullRef<int>(), 100));
                    case 46: return Accesses.Stencil(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.NullRef<int>(), 64), 1, 8);
                    // From index 3 of a span based two elements below address 0 the reads never reach element 2.
                    case 47: return Accesses.Stencil(Accesses.BelowZero(64), 4, 8);
                    case 48: return Accesses.Stencil(Accesses.BelowZero(64), 3, 8);
                    case 49: return Accesses.Above(Accesses.BelowZero(64), 4, 8, 2);
                    case 50: return Accesses.Lookup(new[] { 3, 2, 5, 6 }, Accesses.BelowZero(16), 4);
                    default: return Accesses.Lookup(new[] { 3, 4, 5, 6 }, Accesses.BelowZero(16), 4);
                }
            }

            public const int Count = 52;

            // Runs one group of 30 scenarios: input is the group times 1,000 plus the value they use.
            public static int Run(int input)
            {
                var group = input / 1000;
                input %= 1000;
                var thrown = 0;
                for (var bit = 0; bit < 30 && group * 30 + bit < Count; bit++)
                {
                    var scenario = group * 30 + bit;
                    try
                    {
                        s_sink += Scenario(scenario, input);
                    }
                    catch (NullReferenceException)
                    {
                        thrown |= 1 << bit;
                    }
                    catch (Exception)
                    {
                        return -1 - scenario;
                    }
                }
                // Addresses that are not null still read and write what they address.
                var values = new int[] { 1, 2, 3, input };
                if (Accesses.Sum(values, 1) != 5 + input || Accesses.Read(ref values[3]) != input ||
                    Accesses.Masked(ref values[0], 6) != 1 + 2 + 3 + input + 1 + 2 ||
                    Accesses.Walked(ref values[0], 4) != 1 + 2 + 3 + input || Accesses.Sum(values, 10) != 0 ||
                    Accesses.FoldShifted(values, 1) != (4 ^ 6 ^ input << 1) || Accesses.WalkFold(ref values[0], 4) != 1 + 2 + 3 + input ||
                    Accesses.Hash("abc") != ((17 * 31 + 'a') * 31 + 'b') * 31 + 'c' || Accesses.HashFrom(values, 2) != (17 * 31 + 3) * 31 + input ||
                    Accesses.Find(values, input) != 3 || Accesses.Cursor(values) != (3 ^ 6 ^ 9 ^ input * 3) ||
                    Accesses.PairFields(new[] { new Pair { A = 2, B = 3 }, new Pair { A = 4, B = input } }) != 6 + 4 * input ||
                    Accesses.Ahead(values) != input - 1 || Accesses.CounterAfter(values) != ((31 + 2) * 31 + 3) * 31 + input + 4 ||
                    Accesses.Gather(ref values[0], 4, 2) != 3 || Accesses.Raise(ref values[0], 2, 3) != input ||
                    Accesses.Peak(new[] { 1, 3, 2, 5, 4 }) != 1 || Accesses.Choose(true) != 5 ||
                    Accesses.TernaryStart(values, true) != ((31 + 2) * 31 + 3) * 31 + input ||
                    Accesses.TernaryStart(values, false) != 3 * 31 + input) return -100;
                var wide = new int[128];
                wide[100] = 7;
                wide[4] = input;
                if (Accesses.Arm(ref wide[0]) != 7 * 3 + input) return -102;
                var row = new[] { 1, 2, 3, 4, 5, input };
                var table = new int[16];
                for (var index = 0; index < 16; index++) table[index] = index * 10 + input;
                var keys = new[] { 1, 2, 3, 17 };
                var stencil = 0;
                for (var at = 1; at < 5; at++) stencil += row[at - 1] + row[at] + row[at + 1];
                var above = 0;
                for (var at = 2; at < 6; at++) above += row[at] - row[at - 2];
                var lookup = 0;
                for (var index = 0; index < 4; index++) lookup ^= table[keys[index] & 15] + index;
                if (Accesses.Stencil(row, 1, 4) != stencil || Accesses.Above(row, 2, 4, 2) != above ||
                    Accesses.Lookup(keys, table, 4) != lookup) return -103;
                var sums = new[] { 1, 2, 3, input };
                if (Accesses.Prefix(sums) != 6 + input || sums[2] != 6 || sums[3] != 6 + input) return -101;
                // The cursor's reads before it threw.
                if (group == 0 && Accesses.DownProgress != 3) return -200 - Accesses.DownProgress;
                return thrown;
            }
        }
        """;

    // What O0 does, scenario by scenario, as Mono AOT 11.0.0 does: an access at address 0 throws (the null read, the
    // field access, the span loops from index 0, Volatile and Interlocked, the masked, walked, summed, filled and copied
    // loops, the read of the argument after a read one element in, the elementwise loop from or into a null span, the
    // read at a local's address minus itself, a plain read after an unaligned access, the reduction, elementwise and
    // fold loops from the element of a span that is at 0, the fold over a null span, the walk from a null reference, the
    // versioned span loops over null spans, from the element at 0 and reading one element ahead onto it, a read at one
    // index that is at 0, with a fixed bound and with one the loop raises, a read one element behind its counter from
    // index 1 of a null span, a reference null on one of two paths, a read through a copy of the counter stored in one
    // arm only, the offset loops whose first read is at 0: the stencil over a null span and from index 3 of one based
    // two elements below 0, the row above two elements back onto it, the lookup whose second key is element 2), null
    // plus an offset does not (the read three elements in, the write two in, the span loop from index 3, the loop whose
    // first access is one element in, the stencil from index 4, the lookup whose keys skip element 2), an explicit vector load
    // does not (the vectorized search), nor do Unsafe.ReadUnaligned and WriteUnaligned at address 0 or what reads
    // through them (SequenceCompareTo over null spans), and the cursor makes three reads (addresses 12, 8 and 4) before
    // the fourth throws at 0.
    private const int Count = 52;
    private static readonly int[] Reads = [1, 2, 5, 6, 19, 23, 24, 25, 47, 51];

    // Bit n of a group's result: scenario 30 * group + n threw NullReferenceException.
    private static int ExpectedThrown(int group) => Enumerable.Range(30 * group, Math.Min(30, Count - 30 * group))
        .Where(static scenario => !Reads.Contains(scenario))
        .Aggregate(0, (thrown, scenario) => thrown | 1 << (scenario % 30));

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O1, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void EveryLevelThrowsWhereO0Throws(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("NullManagedAddresses", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        var groups = Enumerable.Range(0, (Count + 29) / 30).ToArray();
        Assert.Equal(groups.Select(ExpectedThrown), groups.Select(group => ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 42 + 1000 * group, target, result.StaticDataEnd)).ToArray());
    }
}
