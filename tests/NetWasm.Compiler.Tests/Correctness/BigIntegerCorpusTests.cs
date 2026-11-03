using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// BigInteger, System.Runtime.Numerics' own code, across its operations: parse
// and format round trips in each style, addition and subtraction across
// signs and carries, multiplication by single limbs and of large values,
// squares, division and remainders of every size, powers and modular powers,
// shifts and bitwise operations of negative values, conversions, comparisons,
// generic math, byte representations and a pidigits loop, against desktop
// .NET 11 on both targets, where wasm32's limbs are 32 bits and wasm64's 64.
// .NET 11's >>> of a negative value, RotateLeft, LeadingZeroCount and a
// negative value's PopCount depend on the limb width, as they do between its
// 32- and 64-bit platforms, so those stay off the desktop comparison.
public sealed class BigIntegerCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void BigIntegerMatchesDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "BigIntegerCorpus",
            "NetWasm.Correctness.BigIntegers",
            Source,
            [.. Enumerable.Range(0, 13)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    private const string Source = """
            using System;
            using System.Globalization;
            using System.Numerics;

            namespace NetWasm.Correctness.BigIntegers;

            public static class EntryPoint
            {
                private static readonly BigInteger[] Values = Build();

                private static BigInteger[] Build()
                {
                    var three = BigInteger.Pow(3, 90);
                    var seven = BigInteger.Pow(7, 61);
                    return
                    [
                        BigInteger.Zero, BigInteger.One, BigInteger.MinusOne, int.MaxValue, int.MinValue, (BigInteger)int.MinValue - 1,
                        uint.MaxValue, (BigInteger)uint.MaxValue + 1, long.MaxValue, long.MinValue, ulong.MaxValue,
                        (BigInteger)ulong.MaxValue + 1, -(BigInteger)ulong.MaxValue, BigInteger.Pow(2, 64) - 1, BigInteger.Pow(2, 127),
                        -BigInteger.Pow(2, 128), three, -three, seven, three * seven + 12345, BigInteger.Pow(10, 40) + 7,
                        BigInteger.Parse("123456789012345678901234567890123456789012345678901234567890"),
                    ];
                }

                public static int Run(int input) => input switch
                {
                    0 => Formats(),
                    1 => AddSubtract(),
                    2 => Multiply(),
                    3 => Divide(),
                    4 => Powers(),
                    5 => Shifts(),
                    6 => Bitwise(),
                    7 => Conversions(),
                    8 => Comparisons(),
                    9 => GenericMath(),
                    10 => Bytes(),
                    11 => Parsing(),
                    12 => PiDigits(300),
                    _ => throw new ArgumentOutOfRangeException(nameof(input)),
                };

                private static int Hash(int hash, string text)
                {
                    foreach (var c in text) hash = unchecked(hash * 31 + c);
                    return unchecked(hash * 31 + text.Length);
                }

                private static int Hash(int hash, BigInteger value) => Hash(hash, value.ToString(CultureInfo.InvariantCulture));

                private static int Formats()
                {
                    var hash = 17;
                    string[] formats = ["", "D", "D50", "X", "X40", "x", "N0", "N3", "E", "E10", "G", "G5", "R", "B", "C2", "P1", "F2"];
                    foreach (var value in Values)
                    {
                        foreach (var format in formats)
                        {
                            hash = Hash(hash, value.ToString(format, CultureInfo.InvariantCulture));
                        }
                    }
                    return hash;
                }

                private static int AddSubtract()
                {
                    var hash = 17;
                    foreach (var left in Values)
                    {
                        foreach (var right in Values)
                        {
                            hash = Hash(hash, left + right);
                            hash = Hash(hash, left - right);
                        }
                    }
                    return hash;
                }

                private static int Multiply()
                {
                    var hash = 17;
                    foreach (var left in Values)
                    {
                        foreach (var right in Values) hash = Hash(hash, left * right);
                        foreach (var small in (int[])[2, 3, 7, -9, 1000003, int.MaxValue]) hash = Hash(hash, left * small);
                        hash = Hash(hash, left * left);
                    }
                    var big = BigInteger.Pow(12345, 400) + 1;
                    return Hash(Hash(Hash(hash, big * big), big * (big - 3)), BigInteger.Pow(big, 3) % 1000000007);
                }

                private static int Divide()
                {
                    var hash = 17;
                    foreach (var dividend in Values)
                    {
                        foreach (var divisor in Values)
                        {
                            if (divisor.IsZero) continue;
                            var quotient = BigInteger.DivRem(dividend, divisor, out var remainder);
                            hash = Hash(Hash(hash, quotient), remainder);
                            hash = Hash(hash, dividend % divisor);
                        }
                        foreach (var small in (int[])[2, 3, 7, -9, 1000003, int.MaxValue]) hash = Hash(Hash(hash, dividend / small), dividend % small);
                    }
                    var value = BigInteger.Pow(3, 2000) + 12345;
                    var by = BigInteger.Pow(7, 600);
                    for (var step = 0; step < 20; step++)
                    {
                        var quotient = BigInteger.DivRem(value, by, out var remainder);
                        hash = Hash(Hash(hash, quotient % 1000000007), remainder % 998244353);
                        hash = unchecked(hash * 31 + (quotient * by + remainder == value ? 1 : 2));
                        value = value * 1000003 + remainder;
                        by += quotient % 977 + 1;
                    }
                    return hash;
                }

                private static int Powers()
                {
                    var hash = 17;
                    foreach (var value in Values)
                    {
                        hash = Hash(hash, BigInteger.Pow(value, 3));
                        hash = Hash(hash, BigInteger.ModPow(BigInteger.Abs(value), 65537, BigInteger.Pow(2, 89) - 1));
                        if (!value.IsZero) hash = Hash(hash, BigInteger.ModPow(7, 1000, BigInteger.Abs(value) + 2));
                    }
                    return Hash(hash, BigInteger.ModPow(BigInteger.Pow(3, 500) + 1, BigInteger.Pow(5, 80), BigInteger.Pow(7, 200) + 4));
                }

                private static int Shifts()
                {
                    var hash = 17;
                    foreach (var value in Values)
                    {
                        foreach (var shift in (int[])[0, 1, 5, 31, 32, 33, 63, 64, 65, 100, -1, -33, -64, -200])
                        {
                            hash = Hash(Hash(hash, value << shift), value >> shift);
                            if (value.Sign >= 0) hash = Hash(hash, value >>> shift);
                        }
                    }
                    return hash;
                }

                private static int Bitwise()
                {
                    var hash = 17;
                    foreach (var left in Values)
                    {
                        hash = Hash(hash, ~left);
                        foreach (var right in Values)
                        {
                            hash = Hash(Hash(Hash(hash, left & right), left | right), left ^ right);
                        }
                    }
                    return hash;
                }

                private static int Conversions()
                {
                    var hash = 17;
                    foreach (var value in Values)
                    {
                        hash = Hash(hash, ((double)value).ToString("R", CultureInfo.InvariantCulture));
                        hash = Hash(hash, ((float)value).ToString("R", CultureInfo.InvariantCulture));
                        hash = Hash(hash, Try(() => ((long)value).ToString(CultureInfo.InvariantCulture)));
                        hash = Hash(hash, Try(() => ((ulong)value).ToString(CultureInfo.InvariantCulture)));
                        hash = Hash(hash, Try(() => ((int)value).ToString(CultureInfo.InvariantCulture)));
                        hash = Hash(hash, Try(() => ((decimal)value).ToString(CultureInfo.InvariantCulture)));
                        hash = Hash(hash, Try(() => ((Int128)value).ToString(CultureInfo.InvariantCulture)));
                    }
                    foreach (var number in (double[])[0.0, -0.5, 1.5, 1e20, -1e300, 123456789.987, double.MaxValue])
                    {
                        hash = Hash(hash, new BigInteger(number));
                    }
                    return Hash(Hash(hash, new BigInteger(79228162514264337593543950335m)), new BigInteger(-1.5e-3m));
                }

                private static string Try(Func<string> convert)
                {
                    try { return convert(); }
                    catch (OverflowException) { return "overflow"; }
                }

                private static int Comparisons()
                {
                    var hash = 17;
                    foreach (var left in Values)
                    {
                        hash = unchecked(hash * 31 + left.Sign);
                        hash = Hash(hash, BigInteger.Abs(left));
                        hash = Hash(hash, -left);
                        foreach (var right in Values)
                        {
                            hash = unchecked(hash * 31 + left.CompareTo(right) * 3 + (left == right ? 1 : 0) + (left.Equals(right) ? 2 : 0));
                            hash = Hash(Hash(hash, BigInteger.Max(left, right)), BigInteger.Min(left, right));
                        }
                    }
                    return hash;
                }

                private static int GenericMath()
                {
                    var hash = 17;
                    foreach (var value in Values)
                    {
                        if (value.Sign >= 0) hash = Hash(hash, BigInteger.PopCount(value));
                        hash = Hash(hash, BigInteger.TrailingZeroCount(value));
                        hash = Hash(hash, BigInteger.Log2(BigInteger.Abs(value) + 1));
                        hash = unchecked(hash * 31 + (int)value.GetBitLength() + (value.IsPowerOfTwo ? 1000 : 0) + (BigInteger.IsPow2(value) ? 2000 : 0));
                        hash = Hash(hash, BigInteger.GreatestCommonDivisor(value, BigInteger.Pow(2, 40) * 9));
                        hash = Hash(hash, BigInteger.Log(BigInteger.Abs(value) + 1).ToString("R", CultureInfo.InvariantCulture));
                        hash = unchecked(hash * 31 + (BigInteger.RotateRight(BigInteger.RotateLeft(value, 7), 7) == value ? 1 : 2));
                        hash = Hash(hash, BigInteger.CopySign(value, -1));
                    }
                    return hash;
                }

                private static int Bytes()
                {
                    var hash = 17;
                    Span<byte> buffer = stackalloc byte[64];
                    foreach (var value in Values)
                    {
                        foreach (var b in value.ToByteArray()) hash = unchecked(hash * 31 + b);
                        foreach (var b in value.ToByteArray(isUnsigned: value.Sign >= 0, isBigEndian: true)) hash = unchecked(hash * 31 + b);
                        hash = unchecked(hash * 31 + value.GetByteCount());
                        if (value.TryWriteBytes(buffer, out var written, isBigEndian: true))
                        {
                            hash = Hash(hash, new BigInteger(buffer[..written], isUnsigned: false, isBigEndian: true));
                        }
                    }
                    return hash;
                }

                private static int Parsing()
                {
                    var hash = 17;
                    string[] texts = ["0", "-0", "  +123  ", "-98765432109876543210987654321", "1e5", "FFFFFFFFFFFFFFFFFFFF", "0x10", "1,234,567",
                        "(42)", "12.0", "abc", "", "   ", "999999999999999999999999999999999999999999999999999999999999"];
                    NumberStyles[] styles = [NumberStyles.Integer, NumberStyles.HexNumber, NumberStyles.Number, NumberStyles.Any, NumberStyles.AllowExponent];
                    foreach (var text in texts)
                    {
                        foreach (var style in styles)
                        {
                            hash = BigInteger.TryParse(text, style, CultureInfo.InvariantCulture, out var value)
                                ? Hash(hash, value)
                                : unchecked(hash * 31 + 7);
                        }
                    }
                    return hash;
                }

                // The Benchmarks Game's pidigits, as the micro-benchmarks run it.
                private static BigInteger q, r, s, t;

                private static int PiDigits(int digits)
                {
                    q = 1; r = 0; s = 0; t = 1;
                    var hash = 17;
                    for (int k = 1, i = 0; i < digits;)
                    {
                        var y = Extract(3);
                        if (y == Extract(4))
                        {
                            hash = unchecked(hash * 31 + y);
                            i++;
                            ComposeRight(10, -10 * y, 0, 1);
                        }
                        else
                        {
                            ComposeLeft(k, 4 * k + 2, 0, 2 * k + 1);
                            k++;
                        }
                    }
                    return hash;
                }

                private static int Extract(int j) => (int)((q * j + r) / (s * j + t));

                private static void ComposeRight(int bq, int br, int bs, int bt)
                {
                    var u = r * bs; r *= bq; var v = t * br; r += v; t *= bt; t += u; s *= bt; u = q * bs; s += u; q *= bq;
                }

                private static void ComposeLeft(int bq, int br, int bs, int bt)
                {
                    r *= bt; var u = q * br; r += u; u = t * bs; t *= bt; var v = s * br; t += v; s *= bq; s += u; q *= bq;
                }

                public static int Trace() => 0;
            }
            """;
}
