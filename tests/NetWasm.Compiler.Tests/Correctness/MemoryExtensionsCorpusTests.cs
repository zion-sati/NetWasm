namespace NetWasm.Compiler.Tests.Correctness;

// MemoryExtensions is upstream's source, which sends span searches, comparisons, splits and
// casing to the vectorized SpanHelpers, Ordinal and InvariantModeCasing paths. Every search
// runs at each length across the vector widths, with its match at every position and with
// none, for byte, char, int, long, string and an IEquatable struct, and matches desktop.
public sealed class MemoryExtensionsCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void MemoryExtensionsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "MemoryExtensionsCorpus",
            "NetWasm.Correctness.MemoryExtensionsSearch",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;

            namespace NetWasm.Correctness.MemoryExtensionsSearch;

            public readonly struct Key : IEquatable<Key>, IComparable<Key>
            {
                public readonly int Value;
                public Key(int value) => Value = value;
                public bool Equals(Key other) => Value == other.Value;
                public int CompareTo(Key other) => Value.CompareTo(other.Value);
                public override bool Equals(object? obj) => obj is Key other && Equals(other);
                public override int GetHashCode() => Value;
            }

            public static class EntryPoint
            {
                private static readonly int[] Lengths = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 40, 63, 64, 65, 100];

                public static int Run(int input) => input switch
                {
                    0 => Searches(static value => (byte)value),
                    1 => Searches(static value => (char)value),
                    2 => Searches(static value => value * 1000003),
                    3 => Searches(static value => value * 0x100000001L),
                    4 => Searches(static value => "s" + value),
                    5 => Searches(static value => new Key(value)),
                    6 => Sequences(),
                    7 => Text(),
                    _ => Splits(),
                };

                // Fill values are 10 to 16; 3 is the value searched for, at every position or
                // nowhere, and 1, 2, 4 to 8 and 200 are values the span does not hold.
                private static int Searches<T>(Func<int, T> make) where T : IEquatable<T>, IComparable<T>
                {
                    T three = make(3), ten = make(10), eleven = make(11), twelve = make(12), sixteen = make(16);
                    T[] two = [make(3), make(200)];
                    T[] three3 = [make(1), make(2), make(3)];
                    T[] four = [make(1), make(2), make(4), make(3)];
                    T[] five = [make(1), make(2), make(4), make(5), make(3)];
                    T[] many = [make(1), make(2), make(4), make(5), make(6), make(7), make(8), make(3)];
                    T[] pair = [make(3), make(11)];
                    var hash = 17;
                    foreach (var length in Lengths)
                    {
                        var data = new T[length];
                        for (var position = -1; position < length; position++)
                        {
                            for (var index = 0; index < length; index++)
                                data[index] = make(index % 7 + 10);
                            if (position >= 0)
                                data[position] = three;
                            ReadOnlySpan<T> span = data;
                            hash = Mix(hash, span.IndexOf(three));
                            hash = Mix(hash, span.LastIndexOf(three));
                            hash = Mix(hash, span.IndexOfAny(two[0], two[1]));
                            hash = Mix(hash, span.IndexOfAny(three3[0], three3[1], three3[2]));
                            hash = Mix(hash, span.IndexOfAny(four));
                            hash = Mix(hash, span.IndexOfAny(five));
                            hash = Mix(hash, span.IndexOfAny(many));
                            hash = Mix(hash, span.LastIndexOfAny(two[0], two[1]));
                            hash = Mix(hash, span.LastIndexOfAny(three3[0], three3[1], three3[2]));
                            hash = Mix(hash, span.LastIndexOfAny(five));
                            hash = Mix(hash, span.IndexOfAnyExcept(ten));
                            hash = Mix(hash, span.IndexOfAnyExcept(ten, eleven));
                            hash = Mix(hash, span.IndexOfAnyExcept(ten, eleven, twelve));
                            hash = Mix(hash, span.LastIndexOfAnyExcept(sixteen));
                            hash = Mix(hash, span.LastIndexOfAnyExcept(ten, eleven));
                            hash = Mix(hash, span.IndexOfAnyExceptInRange(ten, sixteen));
                            hash = Mix(hash, span.LastIndexOfAnyExceptInRange(ten, sixteen));
                            hash = Mix(hash, span.IndexOfAnyInRange(make(0), make(5)));
                            hash = Mix(hash, span.LastIndexOfAnyInRange(make(0), make(5)));
                            hash = Mix(hash, span.Contains(three) ? 1 : 0);
                            hash = Mix(hash, span.ContainsAny(two[0], two[1]) ? 1 : 0);
                            hash = Mix(hash, span.ContainsAnyExcept(ten, eleven, twelve) ? 1 : 0);
                            hash = Mix(hash, span.ContainsAnyInRange(make(0), make(5)) ? 1 : 0);
                            hash = Mix(hash, span.Count(three));
                            hash = Mix(hash, span.Count(pair));
                            hash = Mix(hash, span.IndexOf(pair));
                            hash = Mix(hash, span.LastIndexOf(pair));
                            hash = Mix(hash, span.StartsWith(three) ? 1 : 0);
                            hash = Mix(hash, span.EndsWith(three) ? 1 : 0);
                        }
                    }

                    return hash;
                }

                // Equal and unequal spans at each length, differing at each position.
                private static int Sequences()
                {
                    var hash = 19;
                    foreach (var length in Lengths)
                    {
                        var left = new int[length];
                        var right = new int[length];
                        var bytes = new byte[length];
                        var other = new byte[length];
                        var chars = new char[length];
                        var otherChars = new char[length];
                        for (var position = -1; position < length; position++)
                        {
                            for (var index = 0; index < length; index++)
                            {
                                left[index] = right[index] = index * 31 - 7;
                                bytes[index] = other[index] = (byte)(index * 13);
                                chars[index] = otherChars[index] = (char)('a' + index % 26);
                            }
                            if (position >= 0)
                            {
                                right[position] += position % 2 == 0 ? 1 : -1;
                                other[position] ^= 0x80;
                                otherChars[position] = (char)(otherChars[position] + 1);
                            }
                            hash = Mix(hash, left.AsSpan().SequenceEqual(right) ? 1 : 0);
                            hash = Mix(hash, Math.Sign(left.AsSpan().SequenceCompareTo(right)));
                            hash = Mix(hash, bytes.AsSpan().SequenceEqual(other) ? 1 : 0);
                            hash = Mix(hash, Math.Sign(bytes.AsSpan().SequenceCompareTo(other)));
                            hash = Mix(hash, chars.AsSpan().SequenceEqual(otherChars) ? 1 : 0);
                            hash = Mix(hash, Math.Sign(chars.AsSpan().SequenceCompareTo(otherChars)));
                            hash = Mix(hash, bytes.AsSpan().CommonPrefixLength(other));
                            hash = Mix(hash, left.AsSpan().CommonPrefixLength(right));
                            hash = Mix(hash, chars.AsSpan().StartsWith(otherChars.AsSpan(0, length / 2)) ? 1 : 0);
                            hash = Mix(hash, chars.AsSpan().EndsWith(otherChars.AsSpan(length / 2)) ? 1 : 0);
                            hash = Mix(hash, bytes.AsSpan(0, length / 2).SequenceEqual(other.AsSpan(0, length / 2)) ? 1 : 0);
                        }

                        var reversed = (int[])left.Clone();
                        reversed.AsSpan().Reverse();
                        hash = Mix(hash, reversed);
                        var replaced = (byte[])bytes.Clone();
                        replaced.AsSpan().Replace((byte)13, (byte)7);
                        hash = Mix(hash, replaced);
                        var copied = new char[length];
                        chars.AsSpan().Replace(copied, 'c', 'C');
                        hash = Mix(hash, string.CompareOrdinal(new string(copied), new string(chars)));
                        var sorted = (int[])left.Clone();
                        sorted.AsSpan().Reverse();
                        sorted.AsSpan().Sort();
                        hash = Mix(hash, sorted);
                        hash = Mix(hash, sorted.AsSpan().BinarySearch(length * 31 / 2 - 7));
                        hash = Mix(hash, left.AsSpan().Overlaps(left.AsSpan(length / 2)) ? 1 : 0);
                        hash = Mix(hash, left.AsSpan().Overlaps(right) ? 1 : 0);
                    }

                    return hash;
                }

                // Ordinal and ignore-case comparisons, including letters outside ASCII and
                // surrogate pairs, trimming, white space and invariant casing.
                private static int Text()
                {
                    string[] texts =
                    [
                        "", " ", "a", "Hello, World", "HELLO, world", "  padded\t\r\n", "straße", "STRASSE",
                        "İstanbul", "istanbul", "Ωμέγα", "ΩΜΈΓΑ", "x\U0001F600y", "X\U0001F600Y", "été", "ÉTÉ",
                        "the quick brown fox jumps over the lazy dog", "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG",
                        "line one\nline two\r\nline three\rlast",
                    ];
                    string[] needles = ["", "o", "O", "world", "WORLD", "ß", "SS", "\U0001F600", "lazy", "LAZY DOG", "line", "é", "Ét"];
                    var hash = 23;
                    foreach (var text in texts)
                    {
                        var span = text.AsSpan();
                        foreach (var needle in needles)
                        {
                            foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
                            {
                                hash = Mix(hash, span.IndexOf(needle, comparison));
                                hash = Mix(hash, span.LastIndexOf(needle, comparison));
                                hash = Mix(hash, span.Contains(needle, comparison) ? 1 : 0);
                                hash = Mix(hash, span.StartsWith(needle, comparison) ? 1 : 0);
                                hash = Mix(hash, span.EndsWith(needle, comparison) ? 1 : 0);
                                hash = Mix(hash, span.Equals(needle, comparison) ? 1 : 0);
                                hash = Mix(hash, Math.Sign(span.CompareTo(needle, comparison)));
                            }
                        }
                        foreach (var other in texts)
                        {
                            hash = Mix(hash, span.Equals(other, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                            hash = Mix(hash, Math.Sign(span.CompareTo(other, StringComparison.OrdinalIgnoreCase)));
                        }

                        var upper = new char[text.Length];
                        var lower = new char[text.Length];
                        hash = Mix(hash, span.ToUpperInvariant(upper));
                        hash = Mix(hash, span.ToLowerInvariant(lower));
                        hash = Mix(hash, string.CompareOrdinal(new string(upper), text.ToUpperInvariant()));
                        hash = Mix(hash, string.CompareOrdinal(new string(lower), text.ToLowerInvariant()));
                        hash = Mix(hash, span.ToUpperInvariant(new char[Math.Max(0, text.Length - 1)]));
                        hash = Mix(hash, span.Trim().Length * 100 + span.TrimStart().Length * 10 + span.TrimEnd().Length);
                        hash = Mix(hash, span.Trim("dl ").Length * 100 + span.TrimStart('a').Length * 10 + span.TrimEnd("\r\n").Length);
                        hash = Mix(hash, span.IsWhiteSpace() ? 1 : 0);
                        var lines = 0;
                        foreach (var line in span.EnumerateLines())
                            lines = lines * 31 + line.Length;
                        hash = Mix(hash, lines);
                        var runes = 0;
                        foreach (var rune in span.EnumerateRunes())
                            runes = unchecked(runes * 31 + rune.Value);
                        hash = Mix(hash, runes);
                    }

                    return hash;
                }

                // Splitting into ranges by a character, any of several, a string and any of
                // several strings, with each option.
                private static int Splits()
                {
                    string[] texts = ["", ",", "a", "a,b", ",a,,b,", " a , b ,c ", "a;b,c d", "one--two----three--", "x\ty\nz"];
                    var hash = 29;
                    var ranges = new Range[8];
                    foreach (var text in texts)
                    {
                        var span = text.AsSpan();
                        foreach (var options in new[] { StringSplitOptions.None, StringSplitOptions.RemoveEmptyEntries,
                            StringSplitOptions.TrimEntries, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries })
                        {
                            hash = MixRanges(hash, text, ranges, span.Split(ranges, ',', options));
                            hash = MixRanges(hash, text, ranges, span.SplitAny(ranges, ",; ", options));
                            hash = MixRanges(hash, text, ranges, span.SplitAny(ranges, "", options));
                            hash = MixRanges(hash, text, ranges, span.Split(ranges, "--", options));
                            hash = MixRanges(hash, text, ranges, span.SplitAny(ranges, ["--", ",", ";"], options));
                            hash = MixRanges(hash, text, ranges, span.Split(ranges.AsSpan(0, 2), ',', options));
                        }
                        var pieces = 0;
                        foreach (var range in span.Split(','))
                            pieces = pieces * 31 + span[range].Length;
                        hash = Mix(hash, pieces);
                        foreach (var range in span.SplitAny(',', ';'))
                            pieces = pieces * 31 + span[range].Length;
                        hash = Mix(hash, pieces);
                        foreach (var range in span.Split("--"))
                            pieces = pieces * 31 + span[range].Length;
                        hash = Mix(hash, pieces);
                    }

                    return hash;
                }

                private static int MixRanges(int hash, string text, Range[] ranges, int count)
                {
                    hash = Mix(hash, count);
                    for (var index = 0; index < count; index++)
                    {
                        var (offset, length) = ranges[index].GetOffsetAndLength(text.Length);
                        hash = Mix(hash, offset * 1000 + length);
                    }
                    return hash;
                }

                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

                private static int Mix(int hash, int[] values)
                {
                    foreach (var value in values)
                        hash = Mix(hash, value);
                    return Mix(hash, values.Length);
                }

                private static int Mix(int hash, byte[] values)
                {
                    foreach (var value in values)
                        hash = Mix(hash, value);
                    return Mix(hash, values.Length);
                }

                public static int Trace() => 0;
            }
            """;
}
