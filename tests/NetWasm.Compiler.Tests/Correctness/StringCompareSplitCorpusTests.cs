using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Indexed Compare and CompareOrdinal order null before any string and clamp the length to
// each string's remaining characters; Split treats a null string separator as an empty one,
// skips empty separators and rejects a negative count or an unknown option. Each case returns
// a hash of its result, or a code for the exception it threw.
public sealed class StringCompareSplitCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void CompareAndSplitMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "StringCompareSplitCorpus",
            "NetWasm.Correctness.StringCompareSplit",
            Source,
            [.. Enumerable.Range(0, 32)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;

            namespace NetWasm.Correctness.StringCompareSplit;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Code(() => Math.Sign(string.Compare("abc", 0, "abd", 0, 10, StringComparison.Ordinal))),
                    1 => Code(() => string.Compare("abc", 0, "abc", 0, 10, StringComparison.Ordinal)),
                    2 => Code(() => Math.Sign(string.CompareOrdinal("abc", 0, "abd", 0, 10))),
                    3 => Code(() => Math.Sign(string.Compare("abc", 1, "abcd", 1, 5, StringComparison.Ordinal))),
                    4 => Code(() => Math.Sign(string.Compare(null, 5, "a", 0, 1, StringComparison.Ordinal))),
                    5 => Code(() => string.Compare(null, 0, null, 0, -1, StringComparison.Ordinal)),
                    6 => Code(() => string.Compare("abc", -1, "abc", 0, 1, StringComparison.Ordinal)),
                    7 => Code(() => string.Compare("abc", 4, "abc", 0, 1, StringComparison.Ordinal)),
                    8 => Code(() => string.Compare("abc", 3, "abd", 3, 1, StringComparison.Ordinal)),
                    9 => Code(() => string.Compare("abc", 0, "abc", 0, -1, StringComparison.Ordinal)),
                    10 => Code(() => Math.Sign(string.Compare("ABC", 0, "abd", 0, 10, StringComparison.OrdinalIgnoreCase))),
                    11 => Code(() => string.CompareOrdinal("abc", 4, "abc", 0, 1)),
                    12 => Code(() => string.CompareOrdinal("abc", 0, "abc", 0, -1)),
                    13 => Code(() => Math.Sign(string.CompareOrdinal(null, 0, "a", 0, 1))),
                    14 => Code(() => Math.Sign(string.CompareOrdinal("ab", 1, "abc", 1, int.MaxValue))),
                    15 => Code(() => string.Compare("abc", 0, "abc", 0, 2, (StringComparison)99)),
                    16 => Hash("a b".Split((string?)null)),
                    17 => Hash("a b".Split((string?)null, 2)),
                    18 => Hash("a,b,,c".Split(",", StringSplitOptions.RemoveEmptyEntries)),
                    19 => Hash(" a , b ".Split(',', StringSplitOptions.TrimEntries)),
                    20 => Code(() => "a b".Split(" ", -1).Length),
                    21 => Hash("a b  c\td".Split((char[]?)null)),
                    22 => Hash("a b  c".Split((string[]?)null, StringSplitOptions.None)),
                    23 => Hash("a1b2c".Split(new[] { "1", "", "2" }, StringSplitOptions.None)),
                    24 => Hash("abc".Split("", StringSplitOptions.None)),
                    25 => Hash("a,b,c".Split(',', 2)),
                    26 => Hash("a,b,c".Split(',', 0)),
                    27 => Hash("a\U0001F600b\U0001F600".Split("\U0001F600")),
                    28 => Hash(",a,".Split(',')),
                    29 => Hash("a,,b,c".Split(',', 2, StringSplitOptions.RemoveEmptyEntries)),
                    30 => Code(() => "a".Split(',', (StringSplitOptions)8).Length),
                    31 => Hash(" a ,, b ".Split(new[] { ',', ' ' }, 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Code(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return -100;
                    }
                    catch (ArgumentException)
                    {
                        return -200;
                    }
                }

                private static int Hash(string[] parts)
                {
                    var hash = 17 * 31 + parts.Length;
                    foreach (var part in parts)
                    {
                        foreach (var character in part)
                        {
                            hash = unchecked(hash * 31 + character);
                        }
                        hash = unchecked(hash * 31 + '|');
                    }
                    return hash;
                }
            }
            """;
}
