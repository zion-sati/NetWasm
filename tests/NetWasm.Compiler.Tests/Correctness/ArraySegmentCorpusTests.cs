using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// ArraySegment and the span and memory views over it stay within the segment: each member
// with a start, length, index, range or destination outside the segment throws, a default
// segment throws where .NET checks for one, and a covariant array refuses a span or memory.
// Each case returns a hash of its result, or of the type of the exception it threw and the
// parameter it names.
public sealed class ArraySegmentCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void SegmentViewsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "ArraySegmentCorpus",
            "NetWasm.Correctness.ArraySegmentViews",
            Source,
            [.. Enumerable.Range(0, 56)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.ArraySegmentViews;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Hash(Segment().AsSpan()),
                    1 => Hash(Segment().AsSpan(3)),
                    2 => Hash(Segment().AsSpan(5)),
                    3 => Code(() => Segment().AsSpan(6).Length),
                    4 => Code(() => Segment().AsSpan(-1).Length),
                    5 => Hash(Segment().AsSpan(2, 3)),
                    6 => Code(() => Segment().AsSpan(3, 4).Length),
                    7 => Code(() => Segment().AsSpan(1, -1).Length),
                    8 => Hash(Segment().AsSpan(^2)),
                    9 => Code(() => Segment().AsSpan(^6).Length),
                    10 => Hash(Segment().AsSpan(1..3)),
                    11 => Code(() => Segment().AsSpan(3..6).Length),
                    12 => Hash(Segment().AsMemory(1, 3).Span),
                    13 => Code(() => Segment().AsMemory(3, 4).Length),
                    14 => Code(() => Segment().AsMemory(-1).Length),
                    15 => Hash(Segment().AsMemory(4).Span),
                    16 => Code(() => Segment().AsMemory(6).Length),
                    17 => Hash(Segment().Slice(3).AsSpan()),
                    18 => Code(() => Segment().Slice(6).Count),
                    19 => Code(() => Segment().Slice(2, 4).Count),
                    20 => Hash(Segment().Slice(1, 2).AsSpan()),
                    21 => Code(() => Segment()[5]),
                    22 => Code(() => Segment()[-1]),
                    23 => Code(() =>
                    {
                        var array = Numbers();
                        var segment = new ArraySegment<int>(array, 2, 5);
                        segment[4] = 40;
                        return array[6];
                    }),
                    24 => Code(() => ((IList<int>)Segment())[5]),
                    25 => Code(() => ((IReadOnlyList<int>)Segment())[-1]),
                    26 => Code(() => ((IList<int>)Segment()).IndexOf(0)),
                    27 => Code(() => ((IList<int>)Segment()).IndexOf(4)),
                    28 => Code(() => ((ICollection<int>)Segment()).Contains(8) ? 1 : 0),
                    29 => Hash(Segment().ToArray()),
                    30 => Code(() =>
                    {
                        var destination = new int[6];
                        Segment().CopyTo(destination, 1);
                        return Hash(destination);
                    }),
                    31 => Code(() => new ArraySegment<int>(Numbers(), 8, 3).Count),
                    32 => Code(() => new ArraySegment<int>(Numbers(), -1, 3).Count),
                    33 => Code(() => new ArraySegment<int>(Numbers(), 1, -3).Count),
                    34 => Code(() => new ArraySegment<int>(null!, 0, 0).Count),
                    35 => Code(() => new ArraySegment<int>(null!).Count),
                    36 => Code(() => default(ArraySegment<int>).AsSpan().Length),
                    37 => Code(() => default(ArraySegment<int>).AsSpan(1).Length),
                    38 => Code(() => default(ArraySegment<int>).Slice(0).Count),
                    39 => Code(() => default(ArraySegment<int>)[0]),
                    40 => Code(() => default(ArraySegment<int>).ToArray().Length),
                    41 => Code(() =>
                    {
                        var hash = 17;
                        foreach (var value in Segment())
                        {
                            hash = unchecked(hash * 31 + value);
                        }
                        return hash;
                    }),
                    42 => Code(() => Segment().GetEnumerator().Current),
                    43 => Code(() =>
                    {
                        var enumerator = Segment().GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                        }
                        return enumerator.Current;
                    }),
                    44 => Code(() =>
                    {
                        IEnumerable<int> empty = new ArraySegment<int>(Numbers(), 3, 0);
                        var count = 0;
                        foreach (var _ in empty)
                        {
                            count++;
                        }
                        return count;
                    }),
                    45 => Code(() => new ArraySegment<object>(new[] { "a", "bb" }).AsSpan().Length),
                    46 => Code(() => new ArraySegment<object>(new[] { "a", "bb" }).AsMemory().Length),
                    47 => Code(() => ((string)new ArraySegment<object>(new[] { "a", "bb" }, 1, 1)[0]).Length),
                    48 => Code(() =>
                    {
                        var array = Numbers();
                        return new ArraySegment<int>(array, 2, 5) == new ArraySegment<int>(array, 2, 5) ? 1 : 0;
                    }),
                    49 => Code(() =>
                    {
                        var array = Numbers();
                        return new ArraySegment<int>(array, 2, 5).Equals((object)new ArraySegment<int>(array, 2, 4)) ? 1 : 0;
                    }),
                    50 => Code(() => default(ArraySegment<int>).GetHashCode()),
                    51 => Code(() => ((ArraySegment<int>)(int[])null!).Count),
                    52 => Code(() =>
                    {
                        ICollection<int> collection = Segment();
                        collection.Add(1);
                        return 0;
                    }),
                    53 => Code(() => ((ICollection<int>)Segment()).IsReadOnly ? 1 : 0),
                    54 => Code(() => ArraySegment<int>.Empty.Count + (ArraySegment<int>.Empty.Array is null ? 100 : 0)),
                    55 => Code(() => ((IList<int>)default(ArraySegment<int>)).IndexOf(1)),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int[] Numbers() => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

                // Elements 2 to 6.
                private static ArraySegment<int> Segment() => new(Numbers(), 2, 5);

                private static int Code(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (Exception exception)
                    {
                        var kind = exception switch
                        {
                            ArgumentOutOfRangeException => 1,
                            ArgumentNullException => 2,
                            ArgumentException => 3,
                            InvalidOperationException => 4,
                            ArrayTypeMismatchException => 5,
                            NotSupportedException => 6,
                            _ => 9,
                        };
                        var hash = 17;
                        foreach (var character in (exception as ArgumentException)?.ParamName ?? "")
                        {
                            hash = unchecked(hash * 31 + character);
                        }
                        return -(kind * 1_000_000 + (hash & 0xFFFFF));
                    }
                }

                private static int Hash(ReadOnlySpan<int> values)
                {
                    var hash = 17 * 31 + values.Length;
                    foreach (var value in values)
                    {
                        hash = unchecked(hash * 31 + value);
                    }
                    return hash;
                }
            }
            """;
}
