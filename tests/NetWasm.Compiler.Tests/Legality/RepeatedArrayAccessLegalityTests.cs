using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// An array access or field read skips its checks only when an earlier access
// of the same slots, in its block or one that dominates it, passed them and
// the slots still hold what it checked. Each method breaks one condition,
// and the checks must still throw where desktop .NET throws: the array slot
// given a shorter array, the index changed on a branch, a first access that
// does not dominate, the index written through its address, the object set
// to null on a branch, a handler that catches the first failure and goes on,
// a slot stored in a loop, the next index, the array slot given a shorter
// array within the block of both accesses, the same element accessed only on
// a branch that does not dominate, a local stored at entry and again, and an
// entry block that runs again after the array slot changes.
public sealed class RepeatedArrayAccessLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private sealed class Holder { public int Count; public int Other; }

            private static int Guard(Func<int> body)
            {
                try { return body(); }
                catch (IndexOutOfRangeException) { return -1; }
                catch (NullReferenceException) { return -2; }
            }

            // The array slot is given a shorter array between two accesses of one index.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayReassigned(int[] values, int index, int input)
            {
                var first = values[index];
                if (input % 2 == 1) values = new int[1];
                return first * 100 + values[index];
            }

            // The index slot changes on one branch between two accesses.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int IndexReassigned(int[] values, int index, int input)
            {
                var first = values[index];
                if (input % 2 == 1) index += 10;
                return first * 100 + values[index];
            }

            // The first access is on a branch that does not dominate the second.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NotDominating(int[] values, int index, int input)
            {
                var first = 0;
                if (input % 2 == 0) first = values[index];
                return first * 100 + values[index + input % 3];
            }

            // The index is written through its address between the accesses.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int IndexByAddress(int[] values, int index, int input)
            {
                ref var alias = ref index;
                var first = values[index];
                if (input % 2 == 1) alias = 50;
                return first * 100 + values[index];
            }

            // The object is set to null on one branch between field reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ObjectNulled(Holder? holder, int input)
            {
                var first = holder!.Count;
                if (input % 2 == 1) holder = null;
                return first * 100 + holder!.Other;
            }

            // A handler catches the first failed check, and the method goes on.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CaughtAndContinued(int[] values, int index)
            {
                var first = 0;
                try { first = values[index]; }
                catch (IndexOutOfRangeException) { first = -5; }
                return first * 100 + values[index];
            }

            // The slot is stored in a loop that its accesses repeat in.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StoredInLoop(int[][] arrays, int input)
            {
                var total = 0;
                var values = arrays[0];
                for (var i = 0; i < arrays.Length; i++)
                {
                    total = total * 7 + values[input % 4];
                    values = arrays[i];
                }
                return total * 10 + values[3];
            }

            // An index one past the element the first access checked.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NextIndex(int[] values, int index) => values[index] * 100 + values[index + 1];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int[] Shorter(int[] values, int input) => input % 2 == 1 ? new int[1] : values;

            // The array slot is given a shorter array within the block of both accesses.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReassignedInBlock(int[] values, int index, int input)
            {
                var first = values[index];
                values = Shorter(values, input);
                return first * 100 + values[index];
            }

            // The same element was accessed only on a branch that does not dominate.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SameElementNotDominating(int[] values, int index, int input)
            {
                var first = 0;
                if (input % 2 == 0) first = values[index];
                return first * 100 + values[index];
            }

            // A local stored at entry is stored again on a branch.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StoredTwice(int[] values, int index, int input)
            {
                var local = values;
                var first = local[index];
                if (input % 2 == 1) local = new int[1];
                return first * 100 + local[index];
            }

            // The entry block runs again after the array slot changes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int EntryReentered(int[] values, int index, int input)
            {
            again:
                var local = values;
                var first = local[index];
                if (input-- > 0)
                {
                    values = Shorter(values, input);
                    goto again;
                }
                return first * 100 + local[index];
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var values = new int[] { 1, 2, 3, 4, 5 };
                return which switch
                {
                    0 => Guard(() => ArrayReassigned(values, 3, input)),
                    1 => Guard(() => IndexReassigned(values, 2, input)),
                    2 => Guard(() => NotDominating(values, 3, input)),
                    3 => Guard(() => IndexByAddress(values, 1, input)),
                    4 => Guard(() => ObjectNulled(new Holder { Count = input, Other = 7 }, input)),
                    5 => Guard(() => CaughtAndContinued(values, input * 3)),
                    6 => Guard(() => StoredInLoop([values, new int[] { 9, 8, 7, 6 }, new int[2]], input)),
                    7 => Guard(() => NextIndex(values, input % 5)),
                    8 => Guard(() => ReassignedInBlock(values, 3, input)),
                    9 => Guard(() => SameElementNotDominating(values, input % 2 == 0 ? 2 : 7, input)),
                    10 => Guard(() => StoredTwice(values, 3, input)),
                    11 => Guard(() => EntryReentered(values, 3, input)),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them; -1 and -2 are IndexOutOfRange and NullReference.
    private static readonly (int Input, int Expected)[] Cases = [(0, 404), (1, -1), (2, 404), (3, -1), (4, 404), (100, 303), (101, -1), (102, 303), (103, -1), (104, 303), (200, 404), (201, 5), (202, -1), (203, 4), (204, 405), (300, 202), (301, -1), (302, 202), (303, -1), (304, 202), (400, 7), (401, -2), (402, 207), (403, -2), (404, 407), (500, 101), (501, 404), (502, -1), (503, -1), (504, -1), (600, -1), (601, -1), (602, -1), (603, -1), (604, -1), (700, 102), (701, 203), (702, 304), (703, 405), (704, -1), (800, 404), (801, -1), (802, 404), (803, -1), (804, 404), (900, 303), (901, -1), (902, 303), (903, -1), (904, 303), (1000, 404), (1001, -1), (1002, 404), (1003, -1), (1004, 404), (1100, 404), (1101, 404), (1102, -1), (1103, -1), (1104, -1)];

    [Theory]
    [InlineData(ManagedArrayBoundsCheckElimination.Auto)]
    [InlineData(ManagedArrayBoundsCheckElimination.Enabled)]
    [InlineData(ManagedArrayBoundsCheckElimination.Disabled)]
    public void SkipsAChecksOnlyWhileAnEarlierAccessProvesThem(ManagedArrayBoundsCheckElimination elimination) =>
        LegalityCases.Run($"RepeatedArrayAccessLegality{elimination}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(ArrayBoundsCheckElimination: elimination));
}
