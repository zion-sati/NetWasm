using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// TextInfo is upstream's source, so strings and spans change case through its ASCII paths, which
// read two characters at a time, and fall back to invariant-mode casing from the first non-ASCII
// one. Each case runs over inputs of every length class with the character that must change, and a
// non-ASCII one, at the start, middle and end. The char cases cover the ordinal casing that keeps
// the Kelvin, Ohm and Angstrom signs, and Rune casing. Non-ASCII casing stays on characters whose
// simple mapping desktop's ICU shares with invariant mode; InvariantCasingSweepCompilationTests
// compares every character with .NET 11's invariant mode.
public sealed class TextInfoCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void TextInfoMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "TextInfoCorpus",
            "NetWasm.Correctness.TextInfoCorpus",
            Source,
            [0, 1, 2, 3, 4, 5])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Globalization;
            using System.Text;

            namespace NetWasm.Correctness.TextInfoCorpus;

            public static class EntryPoint
            {
                private static readonly int[] Lengths = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 32, 33, 64, 65];
                private static readonly char[] NonAscii = ['é', 'Ω', 'ж', 'ǅ'];

                public static int Run(int input) => input switch
                {
                    0 => Strings(toUpper: true),
                    1 => Strings(toUpper: false),
                    2 => Spans(),
                    3 => Chars(),
                    4 => TitleCase(),
                    _ => State(),
                };

                public static int Trace() => 0;

                // Letters already in the target case and digits, so only the placed characters change.
                private static string Text(int length, bool upper) =>
                    string.Create(length, upper, static (span, upper) =>
                    {
                        for (var index = 0; index < span.Length; index++)
                            span[index] = index % 3 == 2 ? (char)('0' + index % 10) : (char)((upper ? 'A' : 'a') + index * 7 % 26);
                    });

                private static int Strings(bool toUpper)
                {
                    var hash = 17;
                    var textInfo = CultureInfo.InvariantCulture.TextInfo;
                    foreach (var length in Lengths)
                    {
                        foreach (var changed in new[] { -1, 0, length / 2, length - 1 })
                        {
                            foreach (var nonAscii in new[] { -1, 0, length / 2, length - 1 })
                            {
                                var chars = Text(length, toUpper).ToCharArray();
                                if (length > 0 && changed >= 0) chars[changed] = toUpper ? 'q' : 'Q';
                                if (length > 0 && nonAscii >= 0) chars[nonAscii] = NonAscii[(length + nonAscii) % NonAscii.Length];
                                var source = new string(chars);
                                var results = toUpper
                                    ? new[] { source.ToUpperInvariant(), source.ToUpper(), source.ToUpper(CultureInfo.InvariantCulture), textInfo.ToUpper(source) }
                                    : new[] { source.ToLowerInvariant(), source.ToLower(), source.ToLower(CultureInfo.InvariantCulture), textInfo.ToLower(source) };
                                foreach (var result in results)
                                {
                                    hash = Mix(hash, Sum(result));
                                    // ASCII text that needs no change comes back as the same string.
                                    if (nonAscii < 0) hash = Mix(hash, ReferenceEquals(result, source) ? 1 : 0);
                                }
                            }
                        }
                    }
                    return hash;
                }

                private static int Spans()
                {
                    var hash = 19;
                    foreach (var length in Lengths)
                    {
                        foreach (var nonAscii in new[] { -1, 0, length - 1 })
                        {
                            var chars = (Text(length, upper: false) + Text(length, upper: true)).ToCharArray();
                            if (chars.Length > 0 && nonAscii >= 0) chars[nonAscii] = NonAscii[length % NonAscii.Length];
                            ReadOnlySpan<char> source = chars;
                            foreach (var size in new[] { chars.Length, chars.Length + 3, chars.Length / 2 })
                            {
                                var destination = new char[size];
                                hash = Mix(hash, source.ToUpperInvariant(destination));
                                hash = Mix(hash, Sum(destination));
                                hash = Mix(hash, source.ToLowerInvariant(destination));
                                hash = Mix(hash, Sum(destination));
                                hash = Mix(hash, source.ToUpper(destination, CultureInfo.InvariantCulture));
                                hash = Mix(hash, Sum(destination));
                                hash = Mix(hash, source.ToLower(destination, null));
                                hash = Mix(hash, Sum(destination));
                            }
                        }
                    }
                    var buffer = "abcDEF".ToCharArray();
                    hash = Mix(hash, Code(() => ((ReadOnlySpan<char>)buffer).ToUpperInvariant(buffer)));
                    return Mix(hash, Code(() => ((ReadOnlySpan<char>)buffer.AsSpan(1)).ToLowerInvariant(buffer)));
                }

                private static int Chars()
                {
                    var hash = 23;
                    var textInfo = CultureInfo.InvariantCulture.TextInfo;
                    foreach (var c in "aZ09 ~\u007F\u0080àÿÀÞµαωΑΩςΣаяАЯǄǅǆKΩÅİı𐐀")
                    {
                        hash = Mix(hash, textInfo.ToUpper(c));
                        hash = Mix(hash, textInfo.ToLower(c));
                        hash = Mix(hash, char.ToUpper(c, CultureInfo.InvariantCulture));
                        hash = Mix(hash, char.ToLower(c, CultureInfo.InvariantCulture));
                        hash = Mix(hash, char.ToUpperInvariant(c));
                        hash = Mix(hash, char.ToLowerInvariant(c));
                        hash = Mix(hash, char.ToUpperOrdinal(c));
                        hash = Mix(hash, char.ToLowerOrdinal(c));
                    }
                    var categories = "a\uD801\uDC00 Ω1";
                    for (var index = 0; index < categories.Length; index++)
                    {
                        hash = Mix(hash, (int)char.GetUnicodeCategory(categories, index));
                    }
                    hash = Mix(hash, Code(() => (int)char.GetUnicodeCategory(null!, 0)));
                    hash = Mix(hash, Code(() => (int)char.GetUnicodeCategory("a", 1)));
                    hash = Mix(hash, Code(() => char.ToUpper('a', null!)));
                    hash = Mix(hash, Code(() => char.ToLower('A', null!)));
                    foreach (var value in new[] { 'a', 'Z', 'é', 'Ω', 0x212A, 0x10400, 0x10428, 0x1E922, 0x1F600 })
                    {
                        var rune = new Rune(value);
                        hash = Mix(hash, textInfo.ToUpper(rune).Value);
                        hash = Mix(hash, textInfo.ToLower(rune).Value);
                    }
                    return hash;
                }

                private static int TitleCase()
                {
                    var hash = 29;
                    string[] inputs =
                    [
                        "", "a", "hello world", "HELLO world", "hELLO wORLD", "o'neil mcdonald", "ǆungla ǉubav ǌ", "ǅ",
                        "123abc def", "a-b c_d e.f", "élan VITAL", "αβγ δεζ", "добрый день", "  leading  and trailing  ",
                        "tab\tseparated\nlines", "x\U00010428yz \U00010400ab",
                    ];
                    var textInfo = CultureInfo.InvariantCulture.TextInfo;
                    foreach (var input in inputs)
                    {
                        hash = Mix(hash, Sum(textInfo.ToTitleCase(input)));
                    }
                    return Mix(hash, Code(() => textInfo.ToTitleCase(null!).Length));
                }

                private static int State()
                {
                    var invariant = CultureInfo.InvariantCulture.TextInfo;
                    var hash = Mix(31, invariant.IsReadOnly ? 1 : 0);
                    hash = Mix(hash, Sum(invariant.ListSeparator));
                    hash = Mix(hash, Sum(invariant.CultureName));
                    hash = Mix(hash, Sum(invariant.ToString()));
                    hash = Mix(hash, invariant.IsRightToLeft ? 1 : 0);
                    hash = Mix(hash, invariant.ANSICodePage);
                    hash = Mix(hash, invariant.OEMCodePage);
                    hash = Mix(hash, invariant.MacCodePage);
                    hash = Mix(hash, invariant.EBCDICCodePage);
                    hash = Mix(hash, invariant.LCID);
                    var clone = (TextInfo)invariant.Clone();
                    hash = Mix(hash, clone.IsReadOnly ? 1 : 0);
                    hash = Mix(hash, ReferenceEquals(clone, invariant) ? 1 : 0);
                    hash = Mix(hash, clone.Equals(invariant) ? 1 : 0);
                    hash = Mix(hash, clone.GetHashCode() == invariant.GetHashCode() ? 1 : 0);
                    clone.ListSeparator = ";";
                    hash = Mix(hash, Sum(clone.ListSeparator) * 3 + Sum(invariant.ListSeparator));
                    var readOnly = TextInfo.ReadOnly(clone);
                    hash = Mix(hash, readOnly.IsReadOnly ? 1 : 0);
                    hash = Mix(hash, ReferenceEquals(readOnly, clone) ? 1 : 0);
                    hash = Mix(hash, Sum(readOnly.ListSeparator));
                    hash = Mix(hash, ReferenceEquals(TextInfo.ReadOnly(invariant), invariant) ? 1 : 0);
                    hash = Mix(hash, Code(() => { readOnly.ListSeparator = "|"; return 0; }));
                    hash = Mix(hash, Code(() => { invariant.ListSeparator = "|"; return 0; }));
                    hash = Mix(hash, Code(() => { clone.ListSeparator = null!; return 0; }));
                    hash = Mix(hash, Sum(clone.ToUpper("mixed Case")));
                    hash = Mix(hash, Code(() => invariant.ToUpper((string)null!).Length));
                    hash = Mix(hash, Code(() => invariant.ToLower((string)null!).Length));
                    return Mix(hash, Code(() => TextInfo.ReadOnly(null!).LCID));
                }

                private static int Sum(ReadOnlySpan<char> value)
                {
                    var hash = value.Length;
                    foreach (var c in value) hash = unchecked(hash * 31 + c);
                    return hash;
                }

                private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

                private static int Code(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (ArgumentNullException)
                    {
                        return -300;
                    }
                    catch (ArgumentException)
                    {
                        return -200;
                    }
                    catch (InvalidOperationException)
                    {
                        return -400;
                    }
                }
            }
            """;
}
