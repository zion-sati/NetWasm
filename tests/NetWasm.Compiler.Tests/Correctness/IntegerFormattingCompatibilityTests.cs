namespace NetWasm.Compiler.Tests.Correctness;

// Decimal integer formatting writes two digits at a time from a constant digit
// table, as chars for strings and char spans and as bytes for UTF-8 spans.
public sealed class IntegerFormattingCompatibilityTests(CorrectnessTestRunner runner)
    : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void DecimalFormattingMatchesDesktopAtEveryDigitCount() =>
        Run(new(
            "IntegerFormattingCompatibility",
            "NetWasm.Correctness.IntegerFormattingCompatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7])
        {
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.IntegerFormattingCompatibility;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => Int32Strings(),
                1 => UInt32Strings(),
                2 => Int64Strings(),
                3 => UInt64Strings(),
                4 => PaddedFormats(),
                5 => CharSpans(),
                6 => Utf8Spans(),
                _ => SmallNumbersAndInterpolation(),
            };

            public static int Trace() => 0;

            private static int Int32Strings()
            {
                var hash = Mix(17, int.MinValue.ToString());
                hash = Mix(hash, int.MaxValue.ToString());
                for (var value = 1L; value <= int.MaxValue; value *= 10)
                {
                    foreach (var near in new[] { value - 1, value, value + 1, value * 7 / 3 })
                    {
                        if (near > int.MaxValue) continue;
                        hash = Mix(hash, ((int)near).ToString());
                        hash = Mix(hash, (-(int)near).ToString());
                    }
                }
                return hash;
            }

            private static int UInt32Strings()
            {
                var hash = Mix(17, uint.MaxValue.ToString());
                for (var value = 1UL; value <= uint.MaxValue; value *= 10)
                {
                    foreach (var near in new[] { value - 1, value, value + 1, value * 9 / 4 })
                    {
                        if (near > uint.MaxValue) continue;
                        hash = Mix(hash, ((uint)near).ToString());
                    }
                }
                return hash;
            }

            private static int Int64Strings()
            {
                var hash = Mix(17, long.MinValue.ToString());
                hash = Mix(hash, long.MaxValue.ToString());
                for (var value = 1L; value <= long.MaxValue / 10; value *= 10)
                {
                    foreach (var near in new[] { value - 1, value, value + 1, value * 7 / 3 })
                    {
                        hash = Mix(hash, near.ToString());
                        hash = Mix(hash, (-near).ToString());
                    }
                }
                return hash;
            }

            private static int UInt64Strings()
            {
                var hash = Mix(17, ulong.MaxValue.ToString());
                for (var value = 1UL; value <= ulong.MaxValue / 10; value *= 10)
                {
                    foreach (var near in new[] { value - 1, value, value + 1, value * 9 / 4 })
                    {
                        hash = Mix(hash, near.ToString());
                    }
                }
                return hash;
            }

            private static int PaddedFormats()
            {
                var hash = 17;
                foreach (var value in new[] { 0, 7, 42, 999, 12_345, -6, -98_765, int.MaxValue, int.MinValue })
                {
                    hash = Mix(hash, value.ToString("D"));
                    hash = Mix(hash, value.ToString("D1"));
                    hash = Mix(hash, value.ToString("D6"));
                    hash = Mix(hash, value.ToString("D12"));
                    hash = Mix(hash, ((long)value * 1_000_003).ToString("D20"));
                    hash = Mix(hash, ((ushort)value).ToString());
                    hash = Mix(hash, ((byte)value).ToString("D3"));
                }
                return hash;
            }

            private static int CharSpans()
            {
                var hash = 17;
                Span<char> buffer = stackalloc char[24];
                for (var value = 1L; value <= long.MaxValue / 10; value *= 10)
                {
                    foreach (var near in new[] { value - 1, value, value * 3 + 1 })
                    {
                        if (near.TryFormat(buffer, out var written))
                            hash = Mix(hash, new string(buffer[..written]));
                        if (((int)near).TryFormat(buffer, out written))
                            hash = Mix(hash, new string(buffer[..written]));
                        if (((uint)near).TryFormat(buffer[..3], out written))
                            hash = Mix(hash, new string(buffer[..written]));
                        else
                            hash = hash * 31 + 1;
                    }
                }
                return hash;
            }

            private static int Utf8Spans()
            {
                var hash = 17;
                Span<byte> buffer = stackalloc byte[24];
                for (var value = 1L; value <= long.MaxValue / 10; value *= 10)
                {
                    foreach (var near in new[] { value - 1, value, -value * 3 - 1 })
                    {
                        if (near.TryFormat(buffer, out var written))
                            hash = Mix(hash, buffer[..written]);
                        if (((int)near).TryFormat(buffer, out written))
                            hash = Mix(hash, buffer[..written]);
                    }
                }
                return hash;
            }

            private static int SmallNumbersAndInterpolation()
            {
                var hash = 17;
                for (var value = 0; value < 320; value++)
                {
                    hash = Mix(hash, value.ToString());
                    hash = Mix(hash, $"[{value}:{-value}:{(uint)value * 40_503u}]");
                }
                return hash;
            }

            private static int Mix(int hash, string value)
            {
                foreach (var character in value)
                {
                    hash = hash * 31 + character;
                }
                return hash * 31 + value.Length;
            }

            private static int Mix(int hash, ReadOnlySpan<byte> value)
            {
                foreach (var item in value)
                {
                    hash = hash * 31 + item;
                }
                return hash * 31 + value.Length;
            }
        }
        """;
}
