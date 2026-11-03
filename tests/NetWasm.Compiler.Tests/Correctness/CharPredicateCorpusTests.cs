using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// char's range predicates over every UTF-16 code unit, IsBetween over ranges
// that include reversed and single-value ones, and IsSurrogatePair over the
// code units around both surrogate ranges, against desktop .NET 11.
public sealed class CharPredicateCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void CharPredicatesMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "CharPredicateCorpus",
            "NetWasm.Correctness.CharPredicates",
            Source,
            [.. Enumerable.Range(0, 15)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;

            namespace NetWasm.Correctness.CharPredicates;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Every(char.IsAscii),
                    1 => Every(char.IsAsciiLetter),
                    2 => Every(char.IsAsciiLetterLower),
                    3 => Every(char.IsAsciiLetterUpper),
                    4 => Every(char.IsAsciiDigit),
                    5 => Every(char.IsAsciiLetterOrDigit),
                    6 => Every(char.IsAsciiHexDigit),
                    7 => Every(char.IsAsciiHexDigitLower) * 31 + Every(char.IsAsciiHexDigitUpper),
                    8 => Every(char.IsControl),
                    9 => Every(char.IsSurrogate),
                    10 => Every(char.IsHighSurrogate),
                    11 => Every(char.IsLowSurrogate),
                    12 => Between(),
                    13 => Pairs(),
                    14 => Every(c => char.IsBetween(c, '\uD800', '\uDFFF')),
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };

                private static int Every(Func<char, bool> predicate)
                {
                    var hash = 17;
                    for (var value = 0; value <= char.MaxValue; value++)
                    {
                        if (predicate((char)value)) hash = unchecked(hash * 31 + value);
                    }
                    return hash;
                }

                private static int Between()
                {
                    char[] bounds = ['\0', '\u0001', '0', '9', 'A', 'a', 'z', '\u007F', 'ÿ', '\uD800', '￾', '￿'];
                    var hash = 17;
                    foreach (var minimum in bounds)
                    {
                        foreach (var maximum in bounds)
                        {
                            hash = unchecked(hash * 31 + Every(c => char.IsBetween(c, minimum, maximum)));
                        }
                    }
                    return hash;
                }

                private static int Pairs()
                {
                    char[] units = ['\0', 'A', '퟿', '\uD800', '\uD801', '\uDBFF', '\uDC00', '\uDC01', '\uDFFF', '', '￿'];
                    var hash = 17;
                    foreach (var high in units)
                    {
                        foreach (var low in units)
                        {
                            hash = unchecked(hash * 31 + (char.IsSurrogatePair(high, low) ? 1 : 2));
                        }
                    }
                    return hash;
                }

                public static int Trace() => 0;
            }
            """;
}
