namespace NetWasm.Compiler.Tests.Correctness;

// System.Text.Ascii is upstream's source with the Utf8Utility and Utf16Utility helpers. Every
// operation runs over inputs of each length class, all ASCII and with one non-ASCII value near
// the start, middle and end, so each vector width and scalar tail decides the result.
[Collection(CorrectnessTestGroup.Name)]
public sealed class AsciiCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void AsciiMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "AsciiCorpus",
            "NetWasm.Correctness.AsciiCorpus",
            """
            using System;
            using System.Buffers;
            using System.Text;

            namespace NetWasm.Correctness.AsciiCorpus;

            public static class EntryPoint
            {
                private static readonly int[] Lengths = [0, 1, 3, 4, 7, 8, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 200];

                public static int Run(int input) => input switch
                {
                    0 => Validity(),
                    1 => Casing(),
                    2 => Equality(),
                    3 => Transcoding(),
                    _ => Trimming(),
                };

                private static string Text(int length, int seed) =>
                    string.Create(length, seed, static (span, seed) =>
                    {
                        for (var index = 0; index < span.Length; index++)
                            span[index] = (char)(' ' + (index * 7 + seed) % 95);
                    });

                private static int Validity()
                {
                    var hash = 17;
                    foreach (var length in Lengths)
                    {
                        foreach (var position in new[] { -1, 0, length / 2, length - 1 })
                        {
                            var chars = Text(length, 3).ToCharArray();
                            var bytes = Encoding.ASCII.GetBytes(chars);
                            if (position >= 0 && length > 0)
                            {
                                chars[position] = 'é';
                                bytes[position] = 0xC3;
                            }
                            hash = Mix(hash, Ascii.IsValid(chars) ? 1 : 0);
                            hash = Mix(hash, Ascii.IsValid(bytes) ? 1 : 0);
                        }
                    }
                    return Mix(Mix(hash, Ascii.IsValid('a') ? 1 : 0), Ascii.IsValid((byte)0x80) ? 1 : 0);
                }

                private static int Casing()
                {
                    var hash = 19;
                    foreach (var length in Lengths)
                    {
                        foreach (var position in new[] { -1, length / 2 })
                        {
                            var source = Text(length, 11).ToCharArray();
                            if (position >= 0 && length > 0) source[position] = 'é';
                            var upper = new char[length];
                            var lower = new byte[length];
                            hash = Mix(hash, (int)Ascii.ToUpper(source, upper, out var upperWritten) * 100 + upperWritten);
                            hash = Mix(hash, (int)Ascii.ToLower(source, lower, out var lowerWritten) * 100 + lowerWritten);
                            hash = Mix(hash, Sum(upper, upperWritten));
                            hash = Mix(hash, Sum(lower, lowerWritten));
                            var inPlace = (char[])source.Clone();
                            hash = Mix(hash, (int)Ascii.ToUpperInPlace(inPlace, out var inPlaceWritten) * 100 + inPlaceWritten);
                            hash = Mix(hash, Sum(inPlace, length));
                            var shortDestination = new char[length / 2];
                            hash = Mix(hash, (int)Ascii.ToLower(source, shortDestination, out var shortWritten) * 100 + shortWritten);
                        }
                    }
                    return hash;
                }

                private static int Equality()
                {
                    var hash = 23;
                    foreach (var length in Lengths)
                    {
                        var left = Text(length, 5);
                        var leftBytes = Encoding.ASCII.GetBytes(left);
                        var swapped = new string(Array.ConvertAll(left.ToCharArray(),
                            static c => char.IsAsciiLetter(c) ? (char)(c ^ 0x20) : c));
                        foreach (var position in new[] { -1, 0, length - 1 })
                        {
                            var right = swapped.ToCharArray();
                            if (position >= 0 && length > 0) right[position] = '~' == right[position] ? '}' : '~';
                            var rightBytes = Encoding.ASCII.GetBytes(right);
                            hash = Mix(hash, Ascii.Equals(left, right) ? 1 : 0);
                            hash = Mix(hash, Ascii.EqualsIgnoreCase(left, right) ? 1 : 0);
                            hash = Mix(hash, Ascii.EqualsIgnoreCase(leftBytes, rightBytes) ? 1 : 0);
                            hash = Mix(hash, Ascii.EqualsIgnoreCase(leftBytes, right) ? 1 : 0);
                            hash = Mix(hash, Ascii.Equals(leftBytes, left) ? 1 : 0);
                        }
                    }
                    hash = Mix(hash, Ascii.Equals("ab", "abc") ? 1 : 0);
                    return Mix(hash, Ascii.EqualsIgnoreCase("é", "É") ? 1 : 0);
                }

                private static int Transcoding()
                {
                    var hash = 29;
                    foreach (var length in Lengths)
                    {
                        foreach (var position in new[] { -1, length / 2 })
                        {
                            var chars = Text(length, 7).ToCharArray();
                            if (position >= 0 && length > 0) chars[position] = 'ü';
                            var bytes = new byte[length];
                            hash = Mix(hash, (int)Ascii.FromUtf16(chars, bytes, out var written) * 1000 + written);
                            hash = Mix(hash, Sum(bytes, written));
                            var back = new char[length];
                            var source = (byte[])bytes.Clone();
                            if (position >= 0 && length > 0) source[position] = 0xFC;
                            hash = Mix(hash, (int)Ascii.ToUtf16(source, back, out var decoded) * 1000 + decoded);
                            hash = Mix(hash, Sum(back, decoded));
                            hash = Mix(hash, (int)Ascii.FromUtf16(chars, new byte[length / 3], out var partial) * 1000 + partial);
                        }
                    }
                    return hash;
                }

                private static int Trimming()
                {
                    var hash = 31;
                    foreach (var text in new[] { "", " ", "  a  ", "\t\r\n x y \v\f", "abc", " a ", "   " })
                    {
                        var bytes = Array.ConvertAll(text.ToCharArray(), static c => (byte)c);
                        hash = Mix(hash, Code(Ascii.Trim(text), text.Length));
                        hash = Mix(hash, Code(Ascii.TrimStart(text), text.Length));
                        hash = Mix(hash, Code(Ascii.TrimEnd(text), text.Length));
                        hash = Mix(hash, Code(Ascii.Trim(bytes), bytes.Length));
                    }
                    return hash;
                }

                private static int Code(Range range, int length)
                {
                    var (offset, count) = range.GetOffsetAndLength(length);
                    return offset * 100 + count;
                }

                private static int Sum(char[] values, int count)
                {
                    var hash = 0;
                    for (var index = 0; index < count; index++) hash = unchecked(hash * 31 + values[index]);
                    return hash;
                }

                private static int Sum(byte[] values, int count)
                {
                    var hash = 0;
                    for (var index = 0; index < count; index++) hash = unchecked(hash * 31 + values[index]);
                    return hash;
                }

                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
