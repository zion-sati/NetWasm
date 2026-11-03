using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// double, float, Half and decimal format every standard specifier, hexadecimal included, and
// custom patterns with the provider's NumberFormatInfo: its separators, signs, symbols and
// currency and percent patterns, through ToString, TryFormat, composite formatting and
// interpolation. Without a format or a provider they keep the shortest round-trip text. Each case hashes the strings a value family formats to, so a single wrong
// character fails the case.
public sealed class NumberFormattingCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void FormattedNumbersMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "NumberFormattingCorpus",
            "NetWasm.Correctness.NumberFormatting",
            Source,
            [.. Enumerable.Range(0, 16)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;
            using System.Globalization;

            namespace NetWasm.Correctness.NumberFormatting;

            public static class EntryPoint
            {
                private static readonly string[] Formats =
                {
                    "", "G", "G3", "G17", "R", "F0", "F2", "F7", "E", "e3", "N", "N0", "N3",
                    "P", "P1", "C", "C0", "C3", "X", "x4", "0.00", "#,##0.###", "0.0e+00", "00.0;(00.0);zero", "%0", "0.###E-000",
                };

                private static readonly double[] Doubles =
                {
                    0, -0.0, 1234.5, -1234.5, 0.000123, 1e21, 1.5e-310, double.MaxValue, double.Epsilon,
                    0.125, 2.5, 3.5, 1.0 / 3, double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                };

                // The desktop oracle runs under the machine's culture; the no-provider cases compare
                // the current culture's text, so both sides use the invariant culture.
                public static int Run(int input)
                {
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    return Case(input);
                }

                private static int Case(int input) => input switch
                {
                    0 => HashDoubles(CultureInfo.InvariantCulture),
                    1 => HashDoubles(NumberFormatInfo.InvariantInfo),
                    2 => HashFloats(CultureInfo.InvariantCulture),
                    3 => HashFloats(CultureInfo.CurrentCulture),
                    4 => HashHalves(CultureInfo.InvariantCulture),
                    5 => HashDecimals(CultureInfo.InvariantCulture),
                    6 => HashDecimals(NumberFormatInfo.InvariantInfo),
                    7 => Hash(1234.5.ToString("N2"), (1234.5 / 10000).ToString("P1"), 1234.5.ToString("C"), ((float)1234.5).ToString("N2"), string.Format(CultureInfo.InvariantCulture, "{0:N2}", 1234.5)),
                    8 => Hash(1234.5m.ToString("C", CultureInfo.InvariantCulture), (-1234.5m).ToString("C", CultureInfo.InvariantCulture), (1234.5m / 10000).ToString("P1", CultureInfo.InvariantCulture), 1234.ToString("C", CultureInfo.InvariantCulture)),
                    9 => Hash(1.5.ToString(), (-0.0).ToString(), 0.1.ToString(), 1e21.ToString(), double.MaxValue.ToString(), 1.5f.ToString(), 1.50m.ToString(), (-0.0m).ToString(), $"{-0.00m}", $"{2.25}", $"{2.5f}", $"{2.25m}"),
                    10 => Spans(),
                    11 => Code(() => 1.5.ToString("Y")),
                    12 => Code(() => 1.5.ToString("F1000")),
                    13 => Code(() => 1.5m.ToString("X")),
                    14 => Hash($"{1234.5:N1}", $"{0.25:P0}", $"{-7.5m:C}", $"{3.0f:E2}", string.Format("{0,10:F3}|{1:X}", 2.5, 1.0)),
                    15 => Hash((-1234.5).ToString("#,##0.00;(#,##0.00)"), 0.0.ToString("0.0;-0.0;zero"), 1e-5.ToString("0.###E+0"), 12345.678.ToString("00000.0"), 1.0.ToString("0%")),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int HashDoubles(IFormatProvider provider)
                {
                    var hash = 17;
                    foreach (var value in Doubles)
                    {
                        foreach (var format in Formats)
                        {
                            hash = Mix(hash, Try(() => value.ToString(format, provider)));
                        }
                        hash = Mix(hash, value.ToString(provider));
                    }
                    return hash;
                }

                private static int HashFloats(IFormatProvider provider)
                {
                    var hash = 17;
                    foreach (var value in Doubles)
                    {
                        var single = (float)value;
                        foreach (var format in Formats)
                        {
                            hash = Mix(hash, Try(() => single.ToString(format, provider)));
                        }
                        hash = Mix(hash, single.ToString(provider));
                    }
                    return hash;
                }

                private static int HashHalves(IFormatProvider provider)
                {
                    var hash = 17;
                    foreach (var value in Doubles)
                    {
                        var half = (Half)value;
                        foreach (var format in Formats)
                        {
                            hash = Mix(hash, Try(() => half.ToString(format, provider)));
                        }
                    }
                    return hash;
                }

                private static int HashDecimals(IFormatProvider provider)
                {
                    var hash = 17;
                    foreach (var value in new[] { 0m, -0.0m, 1234.5m, -1234.5m, 0.000123m, 1.50m, decimal.MaxValue, decimal.MinValue, 0.125m, 2.5m, -3.5m, 1m / 3 })
                    {
                        foreach (var format in Formats)
                        {
                            hash = Mix(hash, Try(() => value.ToString(format, provider)));
                        }
                        hash = Mix(hash, value.ToString(provider));
                    }
                    return hash;
                }

                private static int Spans()
                {
                    Span<char> chars = stackalloc char[64];
                    Span<byte> bytes = stackalloc byte[64];
                    var hash = 17;
                    foreach (var format in Formats)
                    {
                        hash = Mix(hash, (-1234.5).TryFormat(chars, out var written, format, CultureInfo.InvariantCulture) ? new string(chars[..written]) : "!");
                        hash = Mix(hash, 1234.5m.TryFormat(chars, out written, format, CultureInfo.InvariantCulture) ? new string(chars[..written]) : "!");
                        hash = Mix(hash, 2.5f.TryFormat(bytes, out written, format, CultureInfo.InvariantCulture) ? written.ToString() : "!");
                        hash = Mix(hash, 1234.5.TryFormat(chars[..3], out written, format, CultureInfo.InvariantCulture) ? new string(chars[..written]) : "short");
                    }
                    return hash;
                }

                private static string Try(Func<string> format)
                {
                    try
                    {
                        return format();
                    }
                    catch (FormatException)
                    {
                        return "FormatException";
                    }
                }

                private static int Code(Func<string> format)
                {
                    try
                    {
                        return Mix(17, format());
                    }
                    catch (FormatException)
                    {
                        return -100;
                    }
                }

                private static int Hash(params string[] values)
                {
                    var hash = 17;
                    foreach (var value in values)
                    {
                        hash = Mix(hash, value);
                    }
                    return hash;
                }

                private static int Mix(int hash, string value)
                {
                    foreach (var character in value)
                    {
                        hash = unchecked(hash * 31 + character);
                    }
                    return unchecked(hash * 31 + '|');
                }
            }
            """;
}
