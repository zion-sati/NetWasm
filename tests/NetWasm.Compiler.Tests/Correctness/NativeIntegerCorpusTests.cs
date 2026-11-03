using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// nint and nuint at the target's width, checked so that desktop's 64-bit
// answer is every target's: BigMul's halves rebuild the exact product of
// operands of that width, and Parse and TryParse honor their styles and
// providers for values every width holds.
public sealed class NativeIntegerCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void NativeIntegersMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "NativeIntegerCorpus",
            "NetWasm.Correctness.NativeIntegers",
            Source,
            [.. Enumerable.Range(0, 4)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;
            using System.Globalization;

            namespace NetWasm.Correctness.NativeIntegers;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => UnsignedProducts(),
                    1 => SignedProducts(),
                    2 => Parsing(),
                    3 => SpanParsing(),
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };

                private static int UnsignedProducts()
                {
                    nuint[] values = [0, 1, 2, 0xFFFF, 0x10000, uint.MaxValue - 1, uint.MaxValue, 0x8000_0000, 0x1234_5678];
                    var hash = 17;
                    foreach (var left in values)
                    {
                        foreach (var right in values)
                        {
                            var high = nuint.BigMul(left, right, out var low);
                            var rebuilt = ((UInt128)(ulong)high << (8 * IntPtr.Size)) | (ulong)low;
                            hash = unchecked(hash * 31 + (rebuilt == (UInt128)(ulong)left * (ulong)right ? 1 : 2));
                        }
                    }
                    return hash;
                }

                private static int SignedProducts()
                {
                    nint[] values = [0, 1, -1, 2, -2, int.MaxValue, int.MinValue, int.MinValue + 1, 0x1234_5678, -0x1234_5678];
                    var hash = 17;
                    foreach (var left in values)
                    {
                        foreach (var right in values)
                        {
                            var high = nint.BigMul(left, right, out var low);
                            var rebuilt = ((Int128)(long)high << (8 * IntPtr.Size)) | (Int128)(ulong)(nuint)low;
                            hash = unchecked(hash * 31 + (rebuilt == (Int128)(long)left * (long)right ? 1 : 2));
                        }
                    }
                    return hash;
                }

                private static int Parsing()
                {
                    var hash = 17;
                    (string Text, NumberStyles Style)[] cases =
                    [
                        ("FF", NumberStyles.HexNumber), (" 7FFFFFFF ", NumberStyles.HexNumber), ("10", NumberStyles.AllowHexSpecifier),
                        ("1,234", NumberStyles.Number), ("(42)", NumberStyles.Any), ("-5", NumberStyles.Integer), ("1e3", NumberStyles.Float),
                        ("101", NumberStyles.AllowBinarySpecifier), ("12.0", NumberStyles.Any), ("abc", NumberStyles.Integer),
                    ];
                    foreach (var (text, style) in cases)
                    {
                        hash = unchecked(hash * 31 + (nuint.TryParse(text, style, CultureInfo.InvariantCulture, out var unsigned) ? (int)unsigned : -1));
                        hash = unchecked(hash * 31 + (nint.TryParse(text, style, CultureInfo.InvariantCulture, out var signed) ? (int)signed : -7));
                        hash = unchecked(hash * 31 + Guard(() => (int)nuint.Parse(text, style, CultureInfo.InvariantCulture)));
                        hash = unchecked(hash * 31 + Guard(() => (int)nint.Parse(text, style, CultureInfo.InvariantCulture)));
                    }
                    return hash;
                }

                private static int SpanParsing()
                {
                    var hash = 17;
                    foreach (var text in (string[])["FF", "7f", "100", "-1", "zz"])
                    {
                        hash = unchecked(hash * 31 + (nuint.TryParse(text.AsSpan(), NumberStyles.HexNumber, null, out var unsigned) ? (int)unsigned : -1));
                        hash = unchecked(hash * 31 + (nint.TryParse(text.AsSpan(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out var signed) ? (int)signed : -7));
                        hash = unchecked(hash * 31 + Guard(() => (int)nuint.Parse(text.AsSpan(), NumberStyles.HexNumber)));
                    }
                    return hash;
                }

                private static int Guard(Func<int> parse)
                {
                    try { return parse(); }
                    catch (FormatException) { return -100; }
                    catch (OverflowException) { return -200; }
                }

                public static int Trace() => 0;
            }
            """;
}
