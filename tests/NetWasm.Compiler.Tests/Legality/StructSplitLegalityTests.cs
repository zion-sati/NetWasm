using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A struct local, a span among them, is split into a local per field only
// where nothing can reach it whole: no address of it escapes, and every copy
// stays a copy. Each method breaks one condition: a span passed by reference
// and changed by the callee, a copy that changes while the original does
// not, a span changed in a try and read in its handler, a struct changed
// through a ref to it, a callee that changes its own copy of a span, fields
// never written, a split struct holding a reference across collections, and
// a vector's lanes read at a variable index and out of range, two structs
// swapped through a tuple, and a tuple deconstructed and kept whole.
public sealed class StructSplitLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;

        public static class EntryPoint
        {
            private struct Pair { public int A; public string? Name; }
            private struct Outer { public Pair Inner; public long Tail; }

            private static int Sum(ReadOnlySpan<int> span)
            {
                var total = 0;
                foreach (var value in span) total = total * 3 + value;
                return total;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Narrow(ref Span<int> span) => span = span[2..];

            // The span is passed by reference and changed by the callee.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChangedByReference(int[] values)
            {
                Span<int> span = values;
                Narrow(ref span);
                return span.Length * 1000 + Sum(span);
            }

            // A copy changes, the original does not.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyChanges(int[] values)
            {
                Span<int> span = values;
                var copy = span;
                copy = copy[3..];
                return span.Length * 1000 + copy.Length * 100 + span[0];
            }

            // The span is changed in a try and read in its handler.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChangedInTry(int[] values, int input)
            {
                Span<int> span = values;
                try
                {
                    span = span[1..];
                    if (input % 2 == 0) throw new InvalidOperationException();
                    span = span[1..];
                }
                catch (InvalidOperationException) { return -span.Length; }
                return span.Length;
            }

            // A ref to the struct local changes it.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChangedThroughRef(int input)
            {
                var pair = new Pair { A = input, Name = "n" };
                ref var alias = ref pair;
                alias.A += 7;
                alias.Name = new string('m', input + 2);
                return pair.A * 100 + pair.Name!.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ChangeArgument(Span<int> span)
            {
                span = span[4..];
                span[0] = -1;
                return span.Length;
            }

            // A callee changes its own copy of the span; the elements it writes are shared.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArgumentCopy(int[] values)
            {
                Span<int> span = values;
                var inner = ChangeArgument(span);
                return span.Length * 1000 + inner * 100 + span[4];
            }

            // Some fields are never written.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PartlyWritten(int input)
            {
                var outer = new Outer();
                outer.Tail = input * 3L;
                return outer.Inner.A + (outer.Inner.Name is null ? 50 : 0) + (int)outer.Tail;
            }

            // A split struct holds a reference across collections.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReferenceSurvives(int input)
            {
                var total = 0;
                for (var i = 0; i < 4; i++)
                {
                    var pair = new Pair { A = i, Name = new string('g', input + i + 1) };
                    GC.Collect();
                    total = total * 7 + pair.A + pair.Name!.Length;
                }
                return total;
            }

            // A vector's lanes at a variable index, and one out of range.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Lanes(int input)
            {
                var vector = Vector128.Create(input, input + 1, input + 2, input + 3);
                var total = 0;
                for (var i = 0; i < 4; i++) total = total * 5 + vector.GetElement(i);
                try { return total + vector.GetElement(input % 6); }
                catch (ArgumentOutOfRangeException) { return -total; }
            }

            // Two struct locals swap through a tuple.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Swap(int input)
            {
                var first = new Pair { A = input, Name = "f" };
                var second = new Pair { A = input + 10, Name = "second" };
                for (var i = 0; i <= input % 3; i++) (first, second) = (second, first);
                return first.A * 1000 + second.A * 10 + first.Name!.Length;
            }

            // A tuple is deconstructed and also used whole.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DeconstructAndKeep(int input)
            {
                var tuple = (input, input * 3);
                var (left, right) = tuple;
                var kept = tuple;
                tuple.Item1 = 99;
                return left * 1000 + right * 10 + kept.Item1 + tuple.Item1;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var values = new int[9 + input];
                for (var i = 0; i < values.Length; i++) values[i] = (i * 37 + input) % 50 + 1;
                return which switch
                {
                    0 => ChangedByReference(values),
                    1 => CopyChanges(values),
                    2 => ChangedInTry(values, input),
                    3 => ChangedThroughRef(input),
                    4 => ArgumentCopy(values),
                    5 => PartlyWritten(input),
                    6 => ReferenceSurvives(input),
                    7 => Lanes(input),
                    8 => Swap(input),
                    9 => DeconstructAndKeep(input),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(0, 33366), (1, 90412), (2, 229649), (3, 701481), (5, 6754798), (100, 9601), (101, 10702), (102, 11803), (103, 12904), (105, 15106), (200, -8), (201, 8), (202, -10), (203, 10), (205, 12), (300, 702), (301, 803), (302, 904), (303, 1005), (305, 1207), (400, 9499), (401, 10599), (402, 11699), (403, 12799), (405, 14999), (500, 50), (501, 53), (502, 56), (503, 59), (505, 65), (600, 532), (601, 932), (602, 1332), (603, 1732), (605, 2532), (700, 38), (701, 196), (702, 354), (703, 512), (705, -818), (800, 10006), (801, 1111), (802, 12026), (803, 13036), (805, 15056), (900, 99), (901, 1130), (902, 2161), (903, 3192), (905, 5254)];

    [Theory]
    [InlineData(ManagedStructScalarReplacement.Auto)]
    [InlineData(ManagedStructScalarReplacement.Enabled)]
    [InlineData(ManagedStructScalarReplacement.Disabled)]
    public void SplitsAStructOnlyWhereNothingReachesItWhole(ManagedStructScalarReplacement replacement) =>
        LegalityCases.Run($"StructSplitLegality{replacement}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(StructScalarReplacement: replacement));
}
