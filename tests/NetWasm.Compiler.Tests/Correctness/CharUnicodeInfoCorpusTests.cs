using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// CharUnicodeInfo reads upstream's three-level Unicode tables through unchecked byte
// offsets. Each case hashes a lookup over every BMP character, or over the supplementary
// planes, so a miscompiled offset changes the hash; the casing cases cover ordinal
// ignore-case search and comparison, including the dotnet/performance search shapes and
// surrogate pairs. Casing stays on characters whose simple mapping desktop's ICU shares
// with invariant mode.
public sealed class CharUnicodeInfoCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void UnicodeLookupsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "CharUnicodeInfoCorpus",
            "NetWasm.Correctness.CharUnicodeInfoLookups",
            Source,
            [.. Enumerable.Range(0, 14)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Globalization;
            using System.Text;

            namespace NetWasm.Correctness.CharUnicodeInfoLookups;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Bmp(c => (int)CharUnicodeInfo.GetUnicodeCategory(c)),
                    1 => Supplementary(codePoint => (int)CharUnicodeInfo.GetUnicodeCategory(codePoint)),
                    2 => Bmp(c => Fold(BitConverter.DoubleToInt64Bits(CharUnicodeInfo.GetNumericValue(c)))),
                    3 => Bmp(CharUnicodeInfo.GetDecimalDigitValue),
                    4 => Bmp(CharUnicodeInfo.GetDigitValue),
                    5 => Bmp(c => (char.IsWhiteSpace(c) ? 1 : 0) | (char.IsLetter(c) ? 2 : 0) |
                        (char.IsDigit(c) ? 4 : 0) | (char.IsPunctuation(c) ? 8 : 0) | (char.IsSymbol(c) ? 16 : 0)),
                    6 => Strings(),
                    7 => Code(() => (int)CharUnicodeInfo.GetUnicodeCategory(0x110000)),
                    8 => Code(() => (int)CharUnicodeInfo.GetUnicodeCategory("a", 1)),
                    9 => Searches(StringComparison.OrdinalIgnoreCase),
                    10 => Searches(StringComparison.Ordinal),
                    11 => Comparisons(),
                    12 => Casing(),
                    13 => Code(() => CharUnicodeInfo.GetDecimalDigitValue(null!, 0)),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Bmp(Func<char, int> lookup)
                {
                    var hash = 17;
                    for (var c = 0; c <= char.MaxValue; c++)
                    {
                        hash = unchecked(hash * 31 + lookup((char)c));
                    }
                    return hash;
                }

                private static int Supplementary(Func<int, int> lookup)
                {
                    var hash = 17;
                    for (var codePoint = 0x10000; codePoint <= 0x10FFFF; codePoint++)
                    {
                        hash = unchecked(hash * 31 + lookup(codePoint));
                    }
                    return hash;
                }

                private static int Fold(long bits) => unchecked((int)bits ^ (int)(bits >> 32));

                // The (string, int) overloads decode surrogate pairs and leave lone surrogates alone.
                private static int Strings()
                {
                    string[] values = ["a1\U0001D7D8\U00010428\uD800x", "\uDC00\uD800", "١½Ⅷ"];
                    var hash = 17;
                    foreach (var value in values)
                    {
                        for (var index = 0; index < value.Length; index++)
                        {
                            hash = unchecked(hash * 31 + (int)CharUnicodeInfo.GetUnicodeCategory(value, index));
                            hash = unchecked(hash * 31 + Fold(BitConverter.DoubleToInt64Bits(CharUnicodeInfo.GetNumericValue(value, index))));
                            hash = unchecked(hash * 31 + CharUnicodeInfo.GetDecimalDigitValue(value, index));
                            hash = unchecked(hash * 31 + CharUnicodeInfo.GetDigitValue(value, index));
                        }
                    }
                    return hash;
                }

                private static int Searches(StringComparison comparison)
                {
                    (string Source, string Value)[] cases =
                    [
                        ("StrIng", "string"),
                        ("More Test's", "Tests"),
                        (Repeat("Hello Worldbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbareallyreallylong", 3) + "!xyz", "w"),
                        (Generate('A', 100, 'X', 70), "x"),
                        (Generate('A', 1000, 'X', 500), "X"),
                        ("αβΓδ", "γΔ"),
                        ("аБв", "бВ"),
                        ("x\U00010400y", "\U00010428Y"),
                        ("x\U00010400", "\uD801"),
                        ("abc", ""),
                        ("", "a"),
                        ("aaaaab", "AB"),
                    ];
                    var hash = 17;
                    foreach (var (source, value) in cases)
                    {
                        hash = unchecked(hash * 31 + source.AsSpan().IndexOf(value.AsSpan(), comparison));
                        hash = unchecked(hash * 31 + source.AsSpan().LastIndexOf(value.AsSpan(), comparison));
                        hash = unchecked(hash * 31 + source.IndexOf(value, comparison));
                        hash = unchecked(hash * 31 + (source.Contains(value, comparison) ? 1 : 0));
                        hash = unchecked(hash * 31 + (source.StartsWith(value, comparison) ? 1 : 0));
                        hash = unchecked(hash * 31 + (source.EndsWith(value, comparison) ? 1 : 0));
                    }
                    return hash;
                }

                private static int Comparisons()
                {
                    (string Left, string Right)[] pairs =
                    [
                        ("Hello", "hELLO"), ("Hello", "Help"), ("été", "ÉTÉ"),
                        ("Σα", "σΑ"), ("\U00010400", "\U00010428"), ("\uD801", "𐐀"),
                        ("a", "ab"), ("", ""), ("аб", "АВ"),
                    ];
                    var hash = 17;
                    foreach (var (left, right) in pairs)
                    {
                        hash = unchecked(hash * 31 + (string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? 1 : 0));
                        hash = unchecked(hash * 31 + Math.Sign(string.Compare(left, right, StringComparison.OrdinalIgnoreCase)));
                        hash = unchecked(hash * 31 + (StringComparer.OrdinalIgnoreCase.GetHashCode(left) ==
                            StringComparer.OrdinalIgnoreCase.GetHashCode(left.ToUpperInvariant()) ? 1 : 0));
                    }
                    return hash;
                }

                private static int Casing()
                {
                    var text = "abcXYZ019 àéÿÀÞµαωΑΩς" +
                        "аяАЯաԱǅ\U00010400\U00010428";
                    var hash = 17;
                    foreach (var value in new[] { text.ToUpperInvariant(), text.ToLowerInvariant() })
                    {
                        foreach (var c in value)
                        {
                            hash = unchecked(hash * 31 + c);
                        }
                    }
                    foreach (var c in text)
                    {
                        hash = unchecked(hash * 31 + char.ToUpperInvariant(c));
                        hash = unchecked(hash * 31 + char.ToLowerInvariant(c));
                    }
                    var rune = new Rune(0x10428);
                    return unchecked(hash * 31 + Rune.ToUpperInvariant(rune).Value * 7 + Rune.ToLowerInvariant(new Rune(0x10400)).Value);
                }

                private static string Repeat(string value, int count)
                {
                    var builder = new StringBuilder();
                    for (var index = 0; index < count; index++) builder.Append(value);
                    return builder.ToString();
                }

                private static string Generate(char source, int count, char replace, int position)
                {
                    var chars = new char[count];
                    for (var index = 0; index < count; index++) chars[index] = source;
                    chars[position] = replace;
                    return new string(chars);
                }

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
                    catch (ArgumentNullException)
                    {
                        return -300;
                    }
                    catch (ArgumentException)
                    {
                        return -200;
                    }
                }
            }
            """;
}
