using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A foreach over a struct array, which the array unroller runs a few
// elements a group, folds an integer sum, or, xor or and into accumulators of
// its own per element only when the split is invisible: wrapping arithmetic
// reassociates, but a handler that reads the sum after an element throws, an
// early exit, a floating sum, a read of the sum in the body, its address and
// a narrow accumulator would each see it. Each method is one of them: a
// shorter second array, a null object and a checked add that overflows
// partway with the handler reading the partial sum, a break
// and a return out of the loop, a double sum, the running sum read in the
// body, the sum passed by reference, a byte sum, the running sum read in the
// body without a branch, directly and through a reference to it, and a sum
// whose expression stores the element it adds.
public sealed class AccumulatorSplitLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            public struct Pair { public int A, B; }
            public struct Node { public int A; public Box? Ref; }
            public sealed class Box { public int Value; }

            // A second, shorter array throws partway; the handler reads the partial sum.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ShorterSecond(Pair[] data, int[] weights)
            {
                var sum = 0;
                try
                {
                    foreach (var pair in data) sum += weights[pair.A % 5] + pair.A;
                }
                catch (IndexOutOfRangeException) { return -sum; }
                return sum;
            }

            // A null object throws partway; the handler reads the partial sum.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NullObject(Node[] data)
            {
                var sum = 0;
                try
                {
                    foreach (var node in data) sum ^= node.Ref!.Value;
                }
                catch (NullReferenceException) { return -sum; }
                return sum;
            }

            // A checked add overflows partway; the handler reads the partial sum.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CheckedOverflow(Pair[] data)
            {
                var sum = 0;
                try
                {
                    foreach (var pair in data) sum = checked(sum + pair.A * 4000003);
                }
                catch (OverflowException) { return -(sum % 100000); }
                return sum;
            }

            // A break leaves the loop with the sum so far.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long BreakOut(Pair[] data, int stop)
            {
                var sum = 0L;
                foreach (var pair in data)
                {
                    if (pair.A == stop) break;
                    sum += pair.B;
                }
                return sum;
            }

            // A return from inside the loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReturnOut(Pair[] data, int stop)
            {
                var bits = 0;
                foreach (var pair in data)
                {
                    bits |= pair.B;
                    if (pair.A == stop) return bits;
                }
                return -bits;
            }

            // A floating sum keeps its order.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FloatingSum(Pair[] data)
            {
                var sum = 0.0;
                foreach (var pair in data) sum += pair.A * 0.1 + 1e15 * (pair.B % 3);
                return (int)(BitConverter.DoubleToInt64Bits(sum) % 1000003);
            }

            // The body reads the running sum.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadInBody(Pair[] data)
            {
                int sum = 0, peak = 0;
                foreach (var pair in data)
                {
                    sum += pair.A - 25;
                    if (sum > peak) peak = sum;
                }
                return peak * 1000 + sum;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Twice(ref int value) => value *= 2;

            // The sum's address is taken.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddressTaken(Pair[] data)
            {
                var sum = 0;
                foreach (var pair in data) sum += pair.B;
                Twice(ref sum);
                return sum;
            }

            // A byte sum wraps at eight bits.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ByteSum(Pair[] data)
            {
                byte sum = 0;
                foreach (var pair in data) sum += (byte)(pair.A * 9);
                return sum;
            }

            // The body reads the running sum without branching.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RunningSumRead(Pair[] data)
            {
                int sum = 0, trace = 0;
                foreach (var pair in data)
                {
                    sum += pair.A;
                    trace = trace * 3 + sum;
                }
                return trace ^ sum;
            }

            // The body reads the sum through a reference to it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReadThroughRef(Pair[] data)
            {
                int sum = 0, trace = 0;
                ref var alias = ref sum;
                foreach (var pair in data)
                {
                    sum += pair.B;
                    trace = trace * 3 + alias;
                }
                return trace ^ sum;
            }

            // The sum's expression stores the element it adds.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StoreInFold(int[] values)
            {
                var sum = 0;
                for (var i = 0; i < values.Length; i++) sum += values[i] = values[i] * 3 + 1;
                var hash = 17;
                foreach (var value in values) hash = unchecked(hash * 31 + value);
                return sum ^ hash;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var data = new Pair[13 + input];
                for (var i = 0; i < data.Length; i++)
                {
                    var value = (i * 37 + input) % 50 + 1;
                    data[i] = new Pair { A = value, B = value * 7 % 31 };
                }
                var nodes = Array.ConvertAll(data, pair => new Node { A = pair.A, Ref = pair.A % 9 == input % 9 ? null : new Box { Value = pair.A } });
                return which switch
                {
                    0 => ShorterSecond(data, [3, 5, 7, 11]),
                    1 => NullObject(nodes),
                    2 => CheckedOverflow(data),
                    3 => (int)BreakOut(data, data[(input * 5 + 6) % data.Length].A),
                    4 => ReturnOut(data, data[(input * 3 + 9) % data.Length].A),
                    5 => FloatingSum(data),
                    6 => ReadInBody(data),
                    7 => AddressTaken(data),
                    8 => ByteSum(data),
                    9 => RunningSumRead(data),
                    10 => ReadThroughRef(data),
                    11 => StoreInFold(Array.ConvertAll(data, pair => pair.A)),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, -102), (1, -9), (2, -91), (3, 0), (6, -14), (100, -3), (101, 0), (102, -63), (103, -60), (106, -35), (200, 1396001047), (201, 1580001185), (202, 1520001140), (203, 1616001212), (206, 1840001380), (300, 73), (301, 227), (302, 21), (303, 72), (306, 203), (400, 31), (401, 31), (402, 21), (403, 30), (406, 31), (500, 124643), (501, 120145), (502, 124645), (503, 129146), (506, 138149), (600, 25024), (601, 45045), (602, 9005), (603, 23004), (606, 6985), (700, 360), (701, 508), (702, 360), (703, 572), (706, 426), (800, 69), (801, 227), (802, 92), (803, 52), (806, 44), (900, 14099922), (901, 45886006), (902, 143992392), (903, 464263452), (906, -2034149192), (1000, 12829814), (1001, 63600013), (1002, 158200405), (1003, 700596791), (1006, -973748501), (1100, 1466022927), (1101, 1359273113), (1102, 1532444567), (1103, 2114424393), (1106, 942187339)];

    [Theory]
    [InlineData(ManagedLoopUnrolling.Auto)]
    [InlineData(ManagedLoopUnrolling.Enabled)]
    [InlineData(ManagedLoopUnrolling.Disabled)]
    public void SplitsAnAccumulatorOnlyWhereNoOneCanSeeTheSplit(ManagedLoopUnrolling unrolling) =>
        LegalityCases.Run($"AccumulatorSplitLegality{unrolling}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(LoopUnrolling: unrolling));
}
