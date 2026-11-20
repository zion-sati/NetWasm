// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A constrained call to a static virtual method that the type does not implement runs the interface's default
// implementation (ECMA-335 II.12.2). The resolver once fell back to an overload with the same stack kinds instead, so
// INumberBase's default TryParse(ReadOnlySpan<byte>, ...) ran Decimal32's TryParse(ReadOnlySpan<char>, ...) over the
// UTF-8 bytes and failed (System.Text.Json's IEEE decimal converters). Overloads here differ only in a span's or an
// array's element type.
public sealed class StaticVirtualDefaultImplementationCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DefaultImplementationsRunWhereTheTypeHasNone() =>
        runner.Run(new(
            "StaticVirtualDefaultImplementationCorpus",
            "NetWasm.Correctness.StaticVirtualDefaults",
            Source,
            [0, 1, 2, 3])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ExpectedReturnValue = 100,
        });

    private const string Source = """
        using System;
        using System.Globalization;
        using System.Numerics;
        using System.Text.Unicode;

        namespace NetWasm.Correctness.StaticVirtualDefaults;

        public interface ISpanParser<T> where T : ISpanParser<T>
        {
            static abstract int Parse(ReadOnlySpan<char> text);

            static virtual int Parse(ReadOnlySpan<byte> utf8)
            {
                Span<char> chars = stackalloc char[32];
                Utf8.ToUtf16(utf8, chars, out _, out var written);
                return T.Parse(chars.Slice(0, written)) + 1000;
            }

            static abstract int Count(int[] values);

            static virtual int Count(long[] values) => values.Length * 10;
        }

        public struct Defaulted : ISpanParser<Defaulted>
        {
            public static int Parse(ReadOnlySpan<char> text) => text.Length == 4 && text[0] == '1' && text[3] == '5' ? 1 : -1;
            public static int Count(int[] values) => values.Length;
        }

        public sealed class Overriding : ISpanParser<Overriding>
        {
            public static int Parse(ReadOnlySpan<char> text) => 2;
            public static int Parse(ReadOnlySpan<byte> utf8) => utf8.Length * 100;
            public static int Count(int[] values) => -1;
            public static int Count(long[] values) => values.Length * 3;
        }

        public static class EntryPoint
        {
            private static int ParseBytes<T>(ReadOnlySpan<byte> utf8) where T : ISpanParser<T> => T.Parse(utf8);
            private static int CountLongs<T>(long[] values) where T : ISpanParser<T> => T.Count(values);

            private static bool TryParseUtf8<T>(ReadOnlySpan<byte> utf8, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T value) where T : INumberBase<T> =>
                T.TryParse(utf8, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);

            private static T ParseUtf8<T>(ReadOnlySpan<byte> utf8) where T : INumberBase<T> =>
                T.Parse(utf8, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

            public static int Run(int input) => input switch
            {
                0 => Custom(),
                1 => Decimals(),
                2 => DirectImplementations(),
                3 => Failures(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int Custom()
            {
                if (ParseBytes<Defaulted>("1.25"u8) != 1001) return 1;
                if (ParseBytes<Overriding>("1.25"u8) != 400) return 2;
                if (CountLongs<Defaulted>(new long[] { 1, 2, 3 }) != 30) return 3;
                if (CountLongs<Overriding>(new long[] { 1, 2, 3 }) != 9) return 4;
                return 100;
            }

            private static int Decimals()
            {
                if (!TryParseUtf8<Decimal32>("1.25"u8, out var d32) || d32 != Decimal32.Parse("1.25", CultureInfo.InvariantCulture)) return 1;
                if (!TryParseUtf8<Decimal64>("-0.001"u8, out var d64) || d64 != Decimal64.Parse("-0.001", CultureInfo.InvariantCulture)) return 2;
                if (!TryParseUtf8<Decimal128>("12345678901234567890.5"u8, out var d128) ||
                    d128 != Decimal128.Parse("12345678901234567890.5", CultureInfo.InvariantCulture)) return 3;
                if (ParseUtf8<Decimal64>("2.5"u8) != Decimal64.Parse("2.5", CultureInfo.InvariantCulture)) return 4;
                return 100;
            }

            private static int DirectImplementations()
            {
                if (!TryParseUtf8<int>("1,234"u8, out var i) || i != 1234) return 1;
                if (!TryParseUtf8<double>("1.5"u8, out var d) || d != 1.5) return 2;
                if (!TryParseUtf8<Half>("0.5"u8, out var h) || (float)h != 0.5f) return 3;
                if (!TryParseUtf8<Int128>("-7"u8, out var big) || big != -7) return 4;
                return 100;
            }

            private static int Failures()
            {
                if (TryParseUtf8<Decimal32>("1.2.5"u8, out _)) return 1;
                if (TryParseUtf8<Decimal64>(new byte[] { 0xFF, 0x31 }, out _)) return 2;
                try
                {
                    ParseUtf8<Decimal32>("abc"u8);
                    return 3;
                }
                catch (FormatException)
                {
                }
                return 100;
            }
        }
        """;
}
