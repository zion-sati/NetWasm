using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A span indexer's check in a counted loop is dropped, and the loop unrolled,
// only when the index starts at a constant of zero or more, steps by one, is
// written nowhere else, and stays within the lookahead its guard proves
// against a length the loop never stores or exposes. Each method breaks one
// condition, and the check must still throw where desktop .NET throws: a
// start below zero, a step of two, another span's length as the bound, an
// access past the guard's lookahead, an index the body moves on, writes up
// to a read one past the end, a span passed by reference and shrunk, a fill
// whose source runs out partway, a constant start below zero, the index one
// behind, a read after the body moves the index on directly and through its
// address, and a read after the body shrinks the span.
public sealed class SpanLoopBoundsLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static int Hash(ReadOnlySpan<int> values)
            {
                var hash = 17;
                foreach (var value in values) hash = unchecked(hash * 31 + value);
                return hash;
            }

            // The index starts below zero.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NegativeStart(Span<int> span, int start)
            {
                var total = 0;
                for (var i = start; i < span.Length; i++) total = total * 3 + span[i];
                return total;
            }

            // The index steps by two under a guard for one ahead.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StepTwo(Span<int> span)
            {
                var total = 0;
                for (var i = 0; i < span.Length; i += 2) total = total * 3 + span[i + 1];
                return total;
            }

            // The bound is another, longer span's length.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OtherBound(Span<int> bound, Span<int> span)
            {
                var total = 0;
                for (var i = 0; i < bound.Length; i++) total = total * 3 + span[i];
                return total;
            }

            // The access looks further ahead than the guard.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PastTheGuard(Span<int> span)
            {
                var total = 0;
                for (var i = 0; i + 1 < span.Length; i++) total = total * 3 + span[i + 2];
                return total;
            }

            // The body moves the index on.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int IndexBumped(Span<int> span, int input)
            {
                var total = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    total = total * 3 + span[i];
                    if (span[i] % 5 == input % 5) i++;
                }
                return total;
            }

            // Each iteration writes, and the last reads one past the end.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OnePast(Span<int> span)
            {
                for (var i = 0; i < span.Length; i++) span[i] = span[i + 1] + i;
                return -1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Shrink(ref Span<int> span, int index)
            {
                if (index == 3) span = span[..2];
            }

            // The span is passed by reference and shrunk inside the loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ShrunkByReference(Span<int> span)
            {
                var total = 0;
                var length = span.Length;
                for (var i = 0; i < length; i++)
                {
                    Shrink(ref span, i);
                    total = total * 3 + span[i];
                }
                return total;
            }

            // A fill whose source runs out partway.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int FillPartway(Span<int> destination, ReadOnlySpan<int> source)
            {
                for (var i = 0; i < destination.Length; i++) destination[i] = source[i] * 2;
                return Hash(destination);
            }

            // The index starts at a constant below zero.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ConstantNegativeStart(Span<int> span)
            {
                var total = 0;
                for (var i = -1; i < span.Length; i++) total = total * 3 + span[i];
                return total;
            }

            // The index one behind the counter.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OneBehind(Span<int> span)
            {
                var total = 0;
                for (var i = 0; i < span.Length; i++) total = total * 3 + span[i - 1];
                return total;
            }

            // The body moves the index on and reads again.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BumpThenRead(Span<int> span, int input)
            {
                var total = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    total = total * 3 + span[i];
                    if (i == span.Length - 1 - input % 2) { i++; total += span[i]; }
                }
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref int index) => index++;

            // The body moves the index on through its address and reads again.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int BumpByReference(Span<int> span, int input)
            {
                var total = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    total = total * 3 + span[i];
                    if (i == span.Length - 1 - input % 2) { Bump(ref i); total += span[i]; }
                }
                return total;
            }

            // The body shrinks the span and reads at the same index.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ShrinkThenRead(Span<int> span)
            {
                var total = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    total = total * 3 + span[i];
                    if (i == span.Length - 1) { span = span[1..]; total += span[i]; }
                }
                return total;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var values = new int[9 + input];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 37 + input) % 50 + 1;
                var target = new int[values.Length];
                try
                {
                    return which switch
                    {
                        0 => NegativeStart(values, input % 2 - 1),
                        1 => StepTwo(values),
                        2 => OtherBound(new int[values.Length + input % 2], values),
                        3 => PastTheGuard(values),
                        4 => IndexBumped(values, input),
                        5 => OnePast(target),
                        6 => ShrunkByReference(values),
                        7 => FillPartway(target, values.AsSpan(0, values.Length - 1 - input % 3)),
                        8 => ConstantNegativeStart(values),
                        9 => OneBehind(values),
                        10 => BumpThenRead(values, input),
                        11 => BumpByReference(values, input),
                        12 => ShrinkThenRead(values),
                        _ => -9,
                    };
                }
                catch (IndexOutOfRangeException) { return -1000 - Hash(target) % 1000; }
                catch (ArgumentOutOfRangeException) { return -2000; }
            }
        }

        """;

    // As desktop .NET 11 computes them; -1000 and below is IndexOutOfRange.
    private static readonly (int Input, int Expected)[] Cases = [(0, -1711), (1, 377657), (2, -927), (3, 3821078), (6, -583), (100, -1711), (101, 3911), (102, -927), (103, 12470), (106, -583), (200, 116033), (201, -969), (202, 1185116), (203, -177), (206, 124656124), (300, -1711), (301, -969), (302, -927), (303, -177), (306, -583), (400, 13714), (401, 44456), (402, 131081), (403, 422777), (406, 4602169), (500, -1587), (501, -853), (502, -314), (503, -708), (506, -360), (600, -1711), (601, -969), (602, -927), (603, -177), (606, -583), (700, -619), (701, -1501), (702, -655), (703, -55), (706, -637), (800, -1711), (801, -969), (802, -927), (803, -177), (806, -583), (900, -1711), (901, -969), (902, -927), (903, -177), (906, -583), (1000, -1711), (1001, 125909), (1002, -927), (1003, 1273700), (1006, -583), (1100, -1711), (1101, 125909), (1102, -927), (1103, 1273700), (1106, -583), (1200, -1711), (1201, -969), (1202, -927), (1203, -177), (1206, -583)];

    public static TheoryData<string> Settings() => ["Auto", "BoundsChecks", "Unrolling"];

    [Theory]
    [MemberData(nameof(Settings))]
    public void DropsASpanCheckOnlyWhenTheLoopProvesIt(string disabled) =>
        LegalityCases.Run($"SpanLoopBoundsLegality{disabled}", Source, Cases, FinalWasmOptimization.O3, disabled switch
        {
            "BoundsChecks" => new WasmOptimizationOverrides(ArrayBoundsCheckElimination: ManagedArrayBoundsCheckElimination.Disabled),
            "Unrolling" => new WasmOptimizationOverrides(LoopUnrolling: ManagedLoopUnrolling.Disabled),
            _ => null,
        });
}
