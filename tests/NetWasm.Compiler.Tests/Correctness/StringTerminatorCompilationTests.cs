// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// A string's characters are followed by a NUL, as CoreCLR lays a string out and native code given a pinned string
// relies on. Each case reads, through a pointer from `fixed` or a pinned GCHandle, the character after the last and how
// many characters precede the first NUL: for literals of odd and even length, the empty string, concatenations,
// substrings, new strings, formatted numbers, StringBuilder and string.Create results, interned and decoded strings, an
// embedded NUL, and strings that survive collections, comparing the results with desktop .NET.
public sealed class StringTerminatorCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StringsEndInANulAsCoreClrLaysThemOut() =>
        runner.Run(new("StringTerminator", "StringTerminator", Source, [0, 1, 7, 42])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
        using System;
        using System.Globalization;
        using System.Runtime.InteropServices;
        using System.Text;
        namespace StringTerminator;

        public static class EntryPoint
        {
            private static int _trace;
            private static int _hash;

            public static int Trace() => _trace;

            private static unsafe void Mix(string value)
            {
                fixed (char* characters = value)
                {
                    var length = 0;
                    while (characters[length] != '\0')
                    {
                        length++;
                    }
                    _hash = unchecked(_hash * 31 + characters[value.Length] + length * 7 + value.Length);
                }
            }

            private static unsafe void MixPinned(string value)
            {
                var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    var characters = (char*)handle.AddrOfPinnedObject();
                    _hash = unchecked(_hash * 31 + characters[value.Length] + 3);
                }
                finally
                {
                    handle.Free();
                }
            }

            public static int Run(int input)
            {
                _hash = 17;
                Mix("literal"); Mix("odd"); Mix("even"); Mix(""); Mix(string.Empty);
                Mix("x" + input); Mix(string.Concat("ab", input.ToString(CultureInfo.InvariantCulture), "cd"));
                Mix("substring of this".Substring(input % 5, 4)); Mix("trimmed   ".TrimEnd());
                Mix(new string('z', input % 9)); Mix(new string(['a', 'b', 'c'], 0, input % 4));
                Mix(input.ToString(CultureInfo.InvariantCulture)); Mix((input * 1.5).ToString(CultureInfo.InvariantCulture));
                Mix(input.ToString("X8", CultureInfo.InvariantCulture)); Mix($"{input}:{input * 2}");
                var builder = new StringBuilder();
                for (var index = 0; index < input % 13; index++)
                {
                    builder.Append((char)('A' + index));
                }
                Mix(builder.ToString());
                Mix(string.Create(input % 6, input, (span, value) =>
                {
                    for (var index = 0; index < span.Length; index++)
                    {
                        span[index] = (char)('0' + (value + index) % 10);
                    }
                }));
                Mix(string.Intern("interned" + (input % 2)));
                Mix(Encoding.UTF8.GetString([0x68, 0x69, (byte)(0x30 + input % 10)]));
                Mix("a\0b");
                Mix("replace me".Replace('e', 'E')); Mix("UPPER".ToLowerInvariant());

                // Strings that survive collections, which may move them.
                var kept = new string[64];
                for (var index = 0; index < kept.Length; index++)
                {
                    kept[index] = new string((char)('a' + index % 26), index % 17 + input % 3);
                }
                GC.Collect();
                GC.Collect();
                foreach (var value in kept)
                {
                    Mix(value);
                }
                MixPinned("pinned literal");
                MixPinned(kept[5]);
                _trace = _hash;
                return _hash;
            }
        }
        """;
}
