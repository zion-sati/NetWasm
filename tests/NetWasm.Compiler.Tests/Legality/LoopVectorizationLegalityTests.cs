using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// An element loop runs four or two iterations a vector only when no
// iteration can throw, every lane computes what its iteration would, and no
// iteration reads what another writes. Each method breaks one condition: one
// array read where the iteration before wrote, and read ahead of where it is
// written, through two parameters and one; a last iteration past the second
// array after the others wrote; a null array, also after an iteration wrote
// another; a floating sum; a division by
// an element that is zero; a checked multiply that overflows; a value the
// loop changes; a fold of an array the loop writes; a walk over fewer
// elements than lanes; a body that moves the counter on; and walks that store
// to an array, only copy the element, read shorts, read a field of the
// element, and fold ints into a long; a float fold, which keeps its order; and
// loops whose element copy is of another lane or of the array they store.
public sealed class LoopVectorizationLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static int Hash(int[] values)
            {
                var hash = 17;
                foreach (var value in values) hash = unchecked(hash * 31 + value);
                return hash;
            }

            // Each iteration reads the element the one before wrote: dst and src are one array.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ShiftUp(int[] dst, int[] src)
            {
                for (var i = 0; i < src.Length - 1; i++) dst[i + 1] = src[i] + 1;
            }

            // Each iteration reads ahead of what it writes: dst and src are one array.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void ShiftDown(int[] dst, int[] src)
            {
                for (var i = 0; i < src.Length - 1; i++) dst[i] = src[i + 1] * 3;
            }

            // The same array is read one ahead of where it is written.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void SameArray(int[] values)
            {
                for (var i = 1; i < values.Length; i++) values[i] = values[i - 1] + 2;
            }

            // The last iteration reads past the second array, after the others wrote.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PastTheEnd(int[] dst, int[] src)
            {
                try
                {
                    for (var i = 0; i < dst.Length; i++) dst[i] = src[i] * 5;
                    return Hash(dst);
                }
                catch (IndexOutOfRangeException) { return -Hash(dst); }
            }

            // A null array throws before any element changes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullArray(int[] dst, int[]? src)
            {
                try
                {
                    for (var i = 0; i < dst.Length; i++) dst[i] = src![i] + 7;
                    return Hash(dst);
                }
                catch (NullReferenceException) { return -Hash(dst); }
            }

            // Each iteration writes one array before it reaches the null one.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WriteBeforeNull(int[] dst, int[]? other, int[] src)
            {
                try
                {
                    for (var i = 0; i < dst.Length; i++) { dst[i] = src[i] + 1; other![i] = src[i]; }
                    return Hash(dst);
                }
                catch (NullReferenceException) { return -Hash(dst); }
            }

            // A floating sum keeps its order.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FloatingSum(double[] values)
            {
                var sum = 0.0;
                for (var i = 0; i < values.Length; i++) sum += values[i];
                return (int)(BitConverter.DoubleToInt64Bits(sum) % 1000003);
            }

            // A division by an element that is zero throws where it is.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DividesByElement(int[] dst, int[] src)
            {
                try
                {
                    for (var i = 0; i < dst.Length; i++) dst[i] = 1000 / src[i];
                    return Hash(dst);
                }
                catch (DivideByZeroException) { return -Hash(dst); }
            }

            // A checked multiply overflows partway.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedOverflow(int[] dst, int[] src)
            {
                try
                {
                    for (var i = 0; i < dst.Length; i++) dst[i] = checked(src[i] * 100000000);
                    return Hash(dst);
                }
                catch (OverflowException) { return -Hash(dst); }
            }

            // A value the loop changes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChangingValue(int[] dst, int seed)
            {
                for (var i = 0; i < dst.Length; i++) { dst[i] = seed; seed = seed * 3 + 1; }
                return Hash(dst);
            }

            // A fold reads an array the loop writes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FoldOfWritten(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < values.Length - 1; i++) { sum += values[i + 1]; values[i] = sum; }
                return Hash(values) ^ sum;
            }

            // A walk over fewer elements than lanes, and none.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ShortWalk(int[] values, int count)
            {
                var sum = 0;
                if (count == 0) return -1;
                ref var p = ref values[0];
                var n = count;
                do { sum += p | 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // The body moves the counter on.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CounterStored(int[] values, int skip)
            {
                var sum = 0;
                for (var i = 0; i < values.Length; i++) { sum += values[i] * 3; i += skip; }
                return sum;
            }

            // A walk that also stores to an array.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkStores(int[] values, int[] first, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { sum += p | 1; first[0] = sum; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum ^ first[0] << 1;
            }

            // A walk that only copies the element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkCopies(int[] values, int count)
            {
                var last = -1;
                ref var p = ref values[0];
                var n = count;
                do { last = p; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return last;
            }

            // A walk over shorts.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkShorts(short[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { sum += p | 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            public struct Pair { public int A, B; }

            // A walk that reads a field of the element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int WalkField(Pair[] values, int count)
            {
                var sum = 0;
                ref var p = ref values[0];
                var n = count;
                do { sum += p.B | 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // A walk that folds ints into a long.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WalkIntoLong(int[] values, int count)
            {
                var sum = 0L;
                ref var p = ref values[0];
                var n = count;
                do { sum += p | 1; p = ref Unsafe.Add(ref p, 1); n--; } while (n != 0);
                return sum;
            }

            // A float fold keeps its order.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float FloatFold(float[] values)
            {
                var sum = 0f;
                for (var i = 0; i < values.Length; i++) sum += values[i] * 0.5f + 1f;
                return sum;
            }

            // A copy of an int element where the loop stores longs.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long CopyOfOtherLane(int[] numbers, long[] target, long[] source)
            {
                var last = 0;
                for (var i = 0; i < target.Length; i++) { last = numbers[i]; target[i] = source[i] + 1; }
                long hash = last;
                foreach (var value in target) hash = unchecked(hash * 31 + value);
                return hash;
            }

            // A copy of an element of the array the loop stores.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyOfStored(int[] target, int[] source)
            {
                var last = 0;
                for (var i = 0; i < target.Length; i++) { last = target[i]; target[i] = source[i] + 1; }
                return Hash(target) ^ last;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var values = new int[11 + input];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 37 + input) % 50 + 1;
                var copy = (int[])values.Clone();
                switch (which)
                {
                    case 0: ShiftUp(copy, copy); return Hash(copy);
                    case 1: ShiftDown(copy, copy); return Hash(copy);
                    case 2: SameArray(copy); return Hash(copy);
                    case 3: return PastTheEnd(new int[values.Length], new int[values.Length - 1 - input % 3]);
                    case 4: return NullArray(copy, null);
                    case 5: return FloatingSum(Array.ConvertAll(values, value => value * 0.1 + 1e15 * (value % 3)));
                    case 6: return DividesByElement(new int[values.Length], Array.ConvertAll(values, value => value % 7 == input % 7 ? 0 : value));
                    case 7: return CheckedOverflow(new int[values.Length], Array.ConvertAll(values, value => value % 30));
                    case 8: return ChangingValue(new int[values.Length], input);
                    case 9: return FoldOfWritten(copy);
                    case 10: return ShortWalk(values, input % 4);
                    case 11: return WriteBeforeNull(new int[values.Length], null, values);
                    case 12: return CounterStored(copy, input % 2);
                    case 13: return WalkStores(values, new int[1], values.Length - input % 3);
                    case 14: return WalkCopies(values, values.Length - input % 3);
                    case 15: return WalkShorts(Array.ConvertAll(values, value => (short)(value * 700)), values.Length - input % 3);
                    case 16: return WalkField(Array.ConvertAll(values, value => new Pair { A = value, B = value * 5 }), values.Length - input % 3);
                    case 17: return (int)(WalkIntoLong(Array.ConvertAll(values, value => value * 90000000), values.Length - input % 3) % 1000000007);
                    case 18: return BitConverter.SingleToInt32Bits(FloatFold(Array.ConvertAll(values, value => value * 0.37f + 1e7f * (value % 3))));
                    case 19: return (int)(CopyOfOtherLane(values, new long[values.Length], Array.ConvertAll(values, value => value * 5000000000L)) % 1000000007);
                    case 20: return CopyOfStored(copy, values);
                    default: return -9;
                }
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 1874343765), (1, 1258142743), (2, -957622760), (3, -1432435240), (6, -1559851490), (100, -1428431909), (101, -72566232), (102, 1836613692), (103, -37283667), (106, 1652480930), (200, -1599664134), (201, 938080285), (202, 2005342942), (203, 225188319), (206, 1464606502), (300, 2100561073), (301, 692883823), (302, 4562033), (303, 141423023), (306, -229639183), (400, -312317171), (401, -79958581), (402, -1351783848), (403, -1439692890), (406, -2098003168), (500, 106641), (501, 115641), (502, 115642), (503, 129144), (506, 133646), (600, 934371630), (601, 1355309092), (602, 790562435), (603, -875868288), (606, 368857959), (700, 1403612081), (701, 124775023), (702, -1105362063), (703, 537316015), (706, -1434354959), (800, 926518766), (801, 640993547), (802, -240826765), (803, 415002554), (806, -1682041491), (900, -819169544), (901, -956437552), (902, -658870096), (903, 896606148), (906, -1714086180), (1000, -1), (1001, 3), (1002, 44), (1003, 75), (1006, 52), (1100, 1399496495), (1101, 305635666), (1102, 1178174061), (1103, -1625751148), (1106, 1828785129), (1200, 888), (1201, 516), (1202, 975), (1203, 546), (1206, 1203), (1300, 887), (1301, 843), (1302, 819), (1303, 900), (1306, 683), (1400, 21), (1401, 22), (1402, 23), (1403, 35), (1406, 49), (1500, 76139), (1501, 83839), (1502, 122075), (1503, 130042), (1506, 215181), (1600, 1485), (1601, 1541), (1602, 1345), (1603, 1872), (1606, 2013), (1700, 870196235), (1701, -434771047), (1702, -649803758), (1703, -84705615), (1706, -564705633), (1800, 1280429825), (1801, 1282929826), (1802, 1282929826), (1803, 1285068426), (1806, 1285693426), (1900, -909601650), (1901, -716964749), (1902, -755876483), (1903, -109340409), (1906, 777256217), (2000, -828704703), (2001, -931980548), (2002, 46441670), (2003, -371207911), (2006, -1657537584)];

    [Theory]
    [InlineData(ManagedLoopVectorization.Auto)]
    [InlineData(ManagedLoopVectorization.Enabled)]
    [InlineData(ManagedLoopVectorization.Disabled)]
    public void VectorizesALoopOnlyWhenNoLaneCanTellTheDifference(ManagedLoopVectorization vectorization) =>
        LegalityCases.Run($"LoopVectorizationLegality{vectorization}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(LoopVectorization: vectorization));
}
