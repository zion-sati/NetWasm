namespace NetWasm.Compiler.Tests.Correctness;

// The byte and char SearchValues are upstream's sources: one to five values, ranges, ASCII sets
// with and without zero, the probabilistic map for non-ASCII sets and the bitmap. Each set
// searches haystacks of every length class with the match near the start, the middle and the
// end, in each direction, for and against the set.
[Collection(CorrectnessTestGroup.Name)]
public sealed class SearchValuesCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SearchValuesMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "SearchValuesCorpus",
            "NetWasm.Correctness.SearchValuesCorpus",
            """
            using System;
            using System.Buffers;

            namespace NetWasm.Correctness.SearchValuesCorpus;

            public static class EntryPoint
            {
                private static readonly int[] Lengths = [0, 1, 2, 7, 8, 15, 16, 17, 31, 32, 33, 63, 64, 65, 100, 255, 300];

                private static readonly string[] CharSets =
                [
                    "a", "ab", "aA", "abc", "abcd", "AaBb", "abcde", "abcdef", "0123456789", "a-z",
                    "\0ab", "\0abcdefgh", "é", "éè", "一丁丂", "aéb一c",
                    "一丁丂七丄丅", "!#$%&*+-/<=>?@^_|~", "￿Ā",
                ];

                public static int Run(int input) => input switch
                {
                    < 19 => Chars(CharSets[input]),
                    19 => Bytes([7]),
                    20 => Bytes([1, 2]),
                    21 => Bytes([1, 2, 3]),
                    22 => Bytes([1, 2, 3, 4]),
                    23 => Bytes([1, 2, 3, 4, 5]),
                    24 => Bytes([10, 11, 12, 13, 14, 15, 16]),
                    25 => Bytes([0, 64, 65, 90, 97, 122]),
                    26 => Bytes([200, 201, 255, 128, 3, 9]),
                    27 => Bytes([]),
                    _ => Strings(),
                };

                private static int Chars(string text)
                {
                    var values = text == "a-z" ? "abcdefghijklmnopqrstuvwxyz" : text;
                    var set = SearchValues.Create(values);
                    var hash = 17;
                    foreach (var length in Lengths)
                    {
                        var haystack = new char[length];
                        for (var index = 0; index < length; index++)
                            haystack[index] = (char)('M' + index % 7);
                        hash = Mix(hash, Search<char>(haystack, set));
                        foreach (var position in new[] { 0, length / 2, length - 1 })
                        {
                            if (position < 0 || values.Length == 0)
                                continue;
                            var copy = (char[])haystack.Clone();
                            copy[position] = values[position % values.Length];
                            hash = Mix(hash, Search<char>(copy, set));
                            var filled = new char[length];
                            filled.AsSpan().Fill(values[0]);
                            filled[position] = 'M';
                            hash = Mix(hash, Search<char>(filled, set));
                        }
                    }

                    foreach (var value in values)
                        hash = Mix(hash, set.Contains(value) ? 1 : 0);
                    return Mix(hash, set.Contains('M') ? 1 : 0);
                }

                private static int Bytes(byte[] values)
                {
                    var set = SearchValues.Create(values);
                    var hash = 23;
                    foreach (var length in Lengths)
                    {
                        var haystack = new byte[length];
                        for (var index = 0; index < length; index++)
                            haystack[index] = (byte)(50 + index % 9);
                        hash = Mix(hash, Search<byte>(haystack, set));
                        foreach (var position in new[] { 0, length / 2, length - 1 })
                        {
                            if (position < 0 || values.Length == 0)
                                continue;
                            var copy = (byte[])haystack.Clone();
                            copy[position] = values[position % values.Length];
                            hash = Mix(hash, Search<byte>(copy, set));
                            var filled = new byte[length];
                            filled.AsSpan().Fill(values[0]);
                            filled[position] = 50;
                            hash = Mix(hash, Search<byte>(filled, set));
                        }
                    }

                    for (var value = 0; value < 256; value++)
                        hash = Mix(hash, set.Contains((byte)value) ? value : 0);
                    return hash;
                }

                private static int Strings()
                {
                    var set = SearchValues.Create(["needle", "pin"], StringComparison.Ordinal);
                    var hash = "a haystack with a pin".AsSpan().IndexOfAny(set) * 100;
                    try
                    {
                        _ = SearchValues.Create(["x"], StringComparison.CurrentCulture);
                        return hash + 1;
                    }
                    catch (ArgumentException exception)
                    {
                        return hash + (exception.ParamName == "comparisonType" ? 2 : 3);
                    }
                }

                private static int Search<T>(T[] haystack, SearchValues<T> set) where T : IEquatable<T>
                {
                    ReadOnlySpan<T> span = haystack;
                    var hash = span.IndexOfAny(set);
                    hash = hash * 7 + span.IndexOfAnyExcept(set);
                    hash = hash * 7 + span.LastIndexOfAny(set);
                    hash = hash * 7 + span.LastIndexOfAnyExcept(set);
                    hash = hash * 7 + (span.ContainsAny(set) ? 1 : 0);
                    return hash * 7 + (span.ContainsAnyExcept(set) ? 1 : 0);
                }

                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

                public static int Trace() => 0;
            }
            """,
            [.. Enumerable.Range(0, 29)])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
