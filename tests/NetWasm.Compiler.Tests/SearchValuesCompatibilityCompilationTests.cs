// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SearchValuesCompatibilityCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesScalarSearchValuesContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"ScalarSearchValues_{target}_{optimize}",
            Source,
            "SearchValuesCompatibilityFixture.EntryPoint",
            optimize,
            target,
            42,
            []));

        Assert.Equal(42, observed);
    }

    private const string Source = """
        using System;
        using System.Buffers;

        namespace SearchValuesCompatibilityFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (!ByteStrategies()) return -1;
                if (!CharStrategies()) return -2;
                if (!StringStrategies()) return -3;
                if (!AhoCorasickStrategies()) return -7;
                var replacementResult = ReplacementContracts();
                if (replacementResult != 0) return replacementResult;
                if (!NullContracts()) return -5;
                if (!SearchesDoNotAllocate()) return -6;
                return input;
            }

            private static bool ByteStrategies()
            {
                ReadOnlySpan<byte> source = new byte[] { 0, 31, 32, 127, 128, 255, 31 };

                if (!Check(
                        SearchValues.Create(ReadOnlySpan<byte>.Empty),
                        source,
                        -1,
                        -1,
                        0,
                        6,
                        0) ||
                    !Check(SearchValues.Create(new byte[] { 31 }), source, 1, 6, 0, 5, 2) ||
                    !Check(SearchValues.Create(new byte[] { 31, 32 }), source, 1, 6, 0, 5, 3) ||
                    !Check(SearchValues.Create(new byte[] { 0, 255 }), source, 0, 5, 1, 6, 2) ||
                    !Check(SearchValues.Create(new byte[] { 0, 32, 128 }), source, 0, 4, 1, 6, 3) ||
                    !Check(SearchValues.Create(new byte[] { 0, 31, 127, 255 }), source, 0, 6, 2, 4, 5) ||
                    !Check(SearchValues.Create(new byte[] { 0, 31, 32, 127, 255 }), source, 0, 6, 4, 4, 6) ||
                    !Check(SearchValues.Create(new byte[] { 0, 31, 32, 127, 128, 255 }), source, 0, 6, -1, -1, 7) ||
                    !Check(SearchValues.Create(new byte[] { 0, 0, 31, 31, 255, 255 }), source, 0, 6, 2, 4, 4))
                {
                    return false;
                }

                var mutable = new byte[] { 3, 17, 250 };
                var immutable = SearchValues.Create(mutable);
                mutable[0] = 99;
                return immutable.Contains(3) && !immutable.Contains(99);
            }

            private static bool CharStrategies()
            {
                ReadOnlySpan<char> source = new char[]
                {
                    '\0', '\u001f', '\u0020', '\u00ff', '\u0100', '\ud800', '\uffff',
                    'x', 'y', 'z',
                };

                if (!Check(
                        SearchValues.Create(new char[] { 'x', 'y', 'z' }),
                        source,
                        7,
                        9,
                        0,
                        6,
                        3) ||
                    !Check(
                        SearchValues.Create(new char[]
                        {
                            '\0', '\u001f', '\u0020', '\u00ff', '\u0100', '\ud800', '\uffff',
                        }),
                        source,
                        0,
                        6,
                        7,
                        9,
                        7))
                {
                    return false;
                }

                var mutable = new char[] { 'a', '\u0100', '\uffff' };
                var immutable = SearchValues.Create(mutable);
                mutable[0] = 'q';
                return immutable.Contains('a') && !immutable.Contains('q');
            }

            private static bool StringStrategies()
            {
                var mutable = new string[] { "--", "|" };
                var ordinal = SearchValues.Create(mutable, StringComparison.Ordinal);
                mutable[0] = "changed";
                ReadOnlySpan<char> source = "aa--bb|cc";
                if (!ordinal.Contains("--") || ordinal.Contains("changed") ||
                    source.IndexOfAny(ordinal) != 2)
                {
                    return false;
                }

                var strings = new string[] { "first", "--", "last", "--" };
                ReadOnlySpan<string> stringSpan = strings;
                if (stringSpan.IndexOfAny(ordinal) != 1 ||
                    stringSpan.LastIndexOfAny(ordinal) != 3 ||
                    stringSpan.IndexOfAnyExcept(ordinal) != 0 ||
                    stringSpan.LastIndexOfAnyExcept(ordinal) != 2)
                {
                    return false;
                }

                var ignoreCase = SearchValues.Create(
                    new string[] { "foo" },
                    StringComparison.OrdinalIgnoreCase);
                if (((ReadOnlySpan<char>)"xxFOOyy").IndexOfAny(ignoreCase) != 2)
                {
                    return false;
                }

                var emptyNeedle = SearchValues.Create(
                    new string[] { "value", string.Empty },
                    StringComparison.Ordinal);
                if (((ReadOnlySpan<char>)string.Empty).IndexOfAny(emptyNeedle) != 0)
                {
                    return false;
                }

                try
                {
                    _ = SearchValues.Create(
                        default(ReadOnlySpan<string>),
                        (StringComparison)123);
                    return false;
                }
                catch (ArgumentException)
                {
                }

                try
                {
                    _ = SearchValues.Create(
                        new string[] { "value", null! },
                        StringComparison.Ordinal);
                    return false;
                }
                catch (ArgumentNullException)
                {
                }

                try
                {
                    _ = SearchValues.Create(
                        new string[] { string.Empty, null! },
                        StringComparison.Ordinal);
                    return false;
                }
                catch (ArgumentNullException)
                {
                }

                return true;
            }

            private static bool AhoCorasickStrategies()
            {
                var earliest = SearchValues.Create(
                    new string[] { "abcd", "bc" },
                    StringComparison.Ordinal);
                if (((ReadOnlySpan<char>)"abcd").IndexOfAny(earliest) != 0 ||
                    ((ReadOnlySpan<char>)"abcx").IndexOfAny(earliest) != 1 ||
                    ((ReadOnlySpan<char>)"abc").IndexOfAny(earliest) != 1)
                {
                    return false;
                }

                var prefixes = SearchValues.Create(
                    new string[] { "abcd", "ab", "ab" },
                    StringComparison.Ordinal);
                ReadOnlySpan<string> originalValues = new string[] { "none", "abcd", "ab" };
                if (!prefixes.Contains("abcd") ||
                    !prefixes.Contains("ab") ||
                    ((ReadOnlySpan<char>)"xxabcd").IndexOfAny(prefixes) != 2 ||
                    originalValues.IndexOfAny(prefixes) != 1 ||
                    originalValues.LastIndexOfAny(prefixes) != 2 ||
                    originalValues.IndexOfAnyExcept(prefixes) != 0 ||
                    originalValues.LastIndexOfAnyExcept(prefixes) != 0)
                {
                    return false;
                }

                var suffix = SearchValues.Create(
                    new string[] { "DOTNET", "OTTER" },
                    StringComparison.Ordinal);
                var inheritedSuffix = SearchValues.Create(
                    new string[] { "POTTERY", "OTTER" },
                    StringComparison.Ordinal);
                if (((ReadOnlySpan<char>)"dotter").IndexOfAny(suffix) != -1 ||
                    ((ReadOnlySpan<char>)"DOTTER").IndexOfAny(suffix) != 1 ||
                    ((ReadOnlySpan<char>)"POTTER").IndexOfAny(inheritedSuffix) != 1)
                {
                    return false;
                }

                var upstreamRegression = SearchValues.Create(
                    new string[] { "IGEdt2", "G", "rIGm6i" },
                    StringComparison.Ordinal);
                if (((ReadOnlySpan<char>)"RyrIGEdt2S9").IndexOfAny(upstreamRegression) != 3)
                {
                    return false;
                }

                var denseAscii = SearchValues.Create(
                    new string[]
                    {
                        "a", "b", "c", "d", "e", "f", "g",
                        "h", "i", "j", "k", "l", "q",
                    },
                    StringComparison.Ordinal);
                var sparseUtf16 = SearchValues.Create(
                    new string[] { "\u0100x", "\ud800y", "\uffffz" },
                    StringComparison.Ordinal);
                if (((ReadOnlySpan<char>)"zzq").IndexOfAny(denseAscii) != 2 ||
                    ((ReadOnlySpan<char>)"0\uffffz").IndexOfAny(sparseUtf16) != 1)
                {
                    return false;
                }

                var first = SearchValues.Create(
                    new string[]
                    {
                        "alpha0", "bravo1", "charlie2", "delta3", "echo4",
                        "foxtrot5", "golf6", "hotel7", "india8", "juliet9",
                        "kilo10", "lambda11", "mike12", "november13", "oscar14",
                        "papa15", "quebec16",
                    },
                    StringComparison.Ordinal);
                if (((ReadOnlySpan<char>)"xxlambda11zz").IndexOfAny(first) != 2)
                {
                    return false;
                }

                var second = SearchValues.Create(
                    new string[] { "red", "green", "blue" },
                    StringComparison.Ordinal);
                return ((ReadOnlySpan<char>)"xxgreen").IndexOfAny(second) == 2 &&
                    ((ReadOnlySpan<char>)"xxlambda11zz").IndexOfAny(first) == 2;
            }

            private static int ReplacementContracts()
            {
                var values = SearchValues.Create(new byte[] { 1, 3, 5, 7, 9, 11 });
                var replaced = new byte[] { 1, 2, 3, 4, 5 };
                replaced.AsSpan().ReplaceAny(values, (byte)42);
                if (!((ReadOnlySpan<byte>)replaced).SequenceEqual(new byte[] { 42, 2, 42, 4, 42 }))
                {
                    return -41;
                }

                var except = new byte[] { 1, 2, 3, 4, 5 };
                except.AsSpan().ReplaceAnyExcept(values, (byte)42);
                if (!((ReadOnlySpan<byte>)except).SequenceEqual(new byte[] { 1, 42, 3, 42, 5 }))
                {
                    return -42;
                }

                var destination = new byte[5];
                ((ReadOnlySpan<byte>)new byte[] { 1, 2, 3, 4, 5 }).ReplaceAny(
                    destination,
                    values,
                    (byte)42);
                if (!((ReadOnlySpan<byte>)destination).SequenceEqual(new byte[] { 42, 2, 42, 4, 42 }))
                {
                    return -43;
                }

                var overlap = new byte[] { 1, 2, 3, 4 };
                try
                {
                    ((ReadOnlySpan<byte>)overlap.AsSpan(0, 3)).ReplaceAny(
                        overlap.AsSpan(1, 3),
                        values,
                        (byte)42);
                    return -44;
                }
                catch (ArgumentException)
                {
                }

                return ((ReadOnlySpan<byte>)overlap).SequenceEqual(new byte[] { 1, 2, 3, 4 })
                    ? 0
                    : -45;
            }

            private static bool NullContracts()
            {
                SearchValues<byte> values = null!;
                try
                {
                    _ = ReadOnlySpan<byte>.Empty.IndexOfAny(values);
                    return false;
                }
                catch (ArgumentNullException)
                {
                }

                try
                {
                    _ = ReadOnlySpan<byte>.Empty.CountAny(values);
                    return false;
                }
                catch (ArgumentNullException)
                {
                }

                try
                {
                    Span<byte>.Empty.ReplaceAny(values, (byte)0);
                    return false;
                }
                catch (ArgumentNullException)
                {
                }

                try
                {
                    Span<byte>.Empty.ReplaceAnyExcept(values, (byte)0);
                    return false;
                }
                catch (ArgumentNullException)
                {
                }

                return true;
            }

            private static bool SearchesDoNotAllocate()
            {
                var values = SearchValues.Create(new byte[] { 0, 31, 32, 127, 128, 255 });
                ReadOnlySpan<byte> source = new byte[] { 1, 2, 3, 31, 5, 255 };
                _ = source.IndexOfAny(values);
                _ = source.LastIndexOfAny(values);
                _ = source.IndexOfAnyExcept(values);
                _ = source.LastIndexOfAnyExcept(values);

                var before = GC.GetTotalAllocatedBytes();
                var checksum = 0;
                for (var index = 0; index < 200; index++)
                {
                    checksum += source.IndexOfAny(values);
                    checksum += source.LastIndexOfAny(values);
                    checksum += source.IndexOfAnyExcept(values);
                    checksum += source.LastIndexOfAnyExcept(values);
                    checksum += source.ContainsAny(values) ? 1 : 0;
                    checksum += source.ContainsAnyExcept(values) ? 1 : 0;
                }

                if (GC.GetTotalAllocatedBytes() - before != 0 || checksum != 2_800)
                {
                    return false;
                }

                var stringValues = SearchValues.Create(
                    new string[] { "--", "|", "tail" },
                    StringComparison.Ordinal);
                ReadOnlySpan<char> stringSource = "zz--payload|tail";
                _ = stringSource.IndexOfAny(stringValues);

                before = GC.GetTotalAllocatedBytes();
                checksum = 0;
                for (var index = 0; index < 200; index++)
                {
                    checksum += stringSource.IndexOfAny(stringValues);
                    checksum += stringSource.ContainsAny(stringValues) ? 1 : 0;
                }

                return GC.GetTotalAllocatedBytes() - before == 0 && checksum == 600;
            }

            private static bool Check<T>(
                SearchValues<T> values,
                ReadOnlySpan<T> source,
                int firstAny,
                int lastAny,
                int firstExcept,
                int lastExcept,
                int count)
                where T : IEquatable<T>
            {
                return source.IndexOfAny(values) == firstAny &&
                    source.LastIndexOfAny(values) == lastAny &&
                    source.IndexOfAnyExcept(values) == firstExcept &&
                    source.LastIndexOfAnyExcept(values) == lastExcept &&
                    source.ContainsAny(values) == (firstAny >= 0) &&
                    source.ContainsAnyExcept(values) == (firstExcept >= 0) &&
                    source.CountAny(values) == count;
            }
        }
        """;
}
