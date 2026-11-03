using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Index and Range are .NET's: a negative index names its value parameter and an
// out-of-range range the length, offsets count from either end, and ranges slice
// spans, strings and arrays through Slice, Substring and the int indexer, as C#
// lowers them for .NET, so an out-of-range span slice throws Slice's exception.
// Each case returns its result, a hash of its text, or a code for the exception it
// threw.
public sealed class IndexRangeCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void IndexAndRangeMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "IndexRangeCorpus",
            "NetWasm.Correctness.IndexRange",
            Source,
            [.. Enumerable.Range(0, 20)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;

            namespace NetWasm.Correctness.IndexRange;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Code(() => new Index(-1).Value),
                    1 => Code(() => Index.FromStart(-1).Value),
                    2 => Code(() => Index.FromEnd(-1).Value),
                    3 => Code(() => (5..2).GetOffsetAndLength(10).Offset),
                    4 => Code(() => (^20..).GetOffsetAndLength(10).Offset),
                    5 => Message(() => new Index(-1, fromEnd: true).Value),
                    6 => (^3).GetHashCode(),
                    7 => (^3).GetOffset(10) * 100 + new Index(4).GetOffset(10),
                    8 => (2..^3).GetOffsetAndLength(10) is var (offset, length) ? offset * 100 + length : -1,
                    9 => Hash((1..^2).ToString()),
                    10 => Hash(Range.All.ToString()),
                    11 => Hash((int.MaxValue..^int.MaxValue).ToString()),
                    12 => (Range.StartAt(2).Equals(2..) ? 1 : 0) + (Range.EndAt(^1).Equals(..^1) ? 2 : 0),
                    13 => (((object)(1..2)).Equals(1..2) ? 1 : 0) + (((object)(1..2)).Equals(1..3) ? 2 : 0),
                    14 => Spans(),
                    15 => Hash("abcdef"[1..^2]),
                    16 => Arrays(),
                    17 => Range.All.GetOffsetAndLength(0).Length + ((Index)5).Value * 10 + (((Index)5).IsFromEnd ? 1000 : 0),
                    18 => Hash((^5).ToString() + Index.Start.ToString() + Index.End.ToString()),
                    19 => Code(() => "abc".AsSpan()[^4..].Length),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Spans()
                {
                    ReadOnlySpan<int> values = [1, 2, 3, 4, 5, 6];
                    Span<int> writable = [7, 8, 9, 10];
                    writable[^1] = 11;
                    return values[1..^1].Length * 1000 + values[^1] * 100 + writable[1..][^1] * 10 + writable[..2][1];
                }

                private static int Arrays()
                {
                    var values = new[] { 1, 2, 3, 4, 5 };
                    var middle = values[1..^1];
                    var sum = 0;
                    foreach (var value in middle) sum += value;
                    return sum * 10 + middle.Length;
                }

                private static int Code(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (ArgumentOutOfRangeException exception)
                    {
                        return -100 - Hash(exception.ParamName ?? "") % 1000;
                    }
                    catch (ArgumentException)
                    {
                        return -200;
                    }
                }

                private static int Message(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (ArgumentOutOfRangeException exception)
                    {
                        return Hash(exception.Message);
                    }
                }

                private static int Hash(string text)
                {
                    var hash = 17;
                    foreach (var character in text)
                    {
                        hash = unchecked(hash * 31 + character);
                    }
                    return hash & 0x7FFFFFFF;
                }
            }
            """;
}
