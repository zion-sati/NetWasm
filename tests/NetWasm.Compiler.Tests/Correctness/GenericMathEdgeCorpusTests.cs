using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// Every exact generic-math member of each primitive type at its edge values:
// MinValue, MaxValue, zero and its neighbours, ties, NaN, infinities and
// subnormals, through the static abstract interfaces as generic code reaches
// them, and the conversions between the types. A member that throws reports
// the exception's kind. NaN results hash alike, since Wasm does not keep the
// NaN sign desktop .NET produces, and members whose result a platform may
// choose (transcendental functions, ConvertToIntegerNative) are left out. Each
// case is one type.
public sealed class GenericMathEdgeCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void GenericMathMatchesDesktopAtEdgeValues(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "GenericMathEdgeCorpus",
            "NetWasm.Correctness.GenericMathEdges",
            Source,
            [.. Enumerable.Range(0, 15)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            ReportAllMismatches = true,
        });

    internal const string Source = """
            using System;
            using System.Numerics;

            namespace NetWasm.Correctness.GenericMathEdges;

            public sealed class Hasher
            {
                private int _hash = 17;
                public void Add(int value) => _hash = unchecked(_hash * 31 + value);
                public void Add(long value) { Add((int)value); Add((int)(value >> 32)); }
                public void Add(bool value) => Add(value ? 1 : 2);
                public int Value => _hash;
            }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Integer(new sbyte[] { sbyte.MinValue, -100, -3, -1, 0, 1, 3, 64, sbyte.MaxValue }),
                    1 => Integer(new byte[] { 0, 1, 3, 64, 127, 128, 200, byte.MaxValue }),
                    2 => Integer(new short[] { short.MinValue, -1000, -3, -1, 0, 1, 3, 256, short.MaxValue }),
                    3 => Integer(new ushort[] { 0, 1, 3, 256, 32767, 32768, ushort.MaxValue }),
                    4 => Integer(new int[] { int.MinValue, int.MinValue + 1, -65536, -3, -1, 0, 1, 3, 65536, int.MaxValue }),
                    5 => Integer(new uint[] { 0, 1, 3, 65536, int.MaxValue, 2147483648u, uint.MaxValue }),
                    6 => Integer(new long[] { long.MinValue, long.MinValue + 1, -4294967296, -3, -1, 0, 1, 3, 4294967296, long.MaxValue }),
                    7 => Integer(new ulong[] { 0, 1, 3, 4294967296, long.MaxValue, 9223372036854775808ul, ulong.MaxValue }),
                    8 => Integer(new char[] { '\0', 'A', 'z', 'ÿ', 'Ā', '翿', '耀', '￿' }),
                    9 => Integer(new Int128[] { Int128.MinValue, -((Int128)1 << 64), -3, -1, 0, 1, 3, (Int128)1 << 64, Int128.MaxValue }),
                    10 => Integer(new UInt128[] { 0, 1, 3, (UInt128)1 << 64, UInt128.MaxValue >> 1, UInt128.MaxValue }),
                    11 => Floating(new float[] { 0f, -0f, 1f, -1f, 0.5f, 1.5f, 2.5f, -2.5f, float.Epsilon, -float.Epsilon, float.Epsilon * 3, 1e-38f, float.MinValue, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 123.456f, 1e10f }),
                    12 => Floating(new double[] { 0d, -0d, 1d, -1d, 0.5, 1.5, 2.5, -2.5, double.Epsilon, -double.Epsilon, double.Epsilon * 3, 1e-308, double.MinValue, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 123.456, 1e300 }),
                    13 => Floating(new Half[] { (Half)0f, (Half)(-0f), (Half)1f, (Half)(-1f), (Half)0.5f, (Half)1.5f, (Half)2.5f, Half.Epsilon, Half.MinValue, Half.MaxValue, Half.NaN, Half.PositiveInfinity, Half.NegativeInfinity, (Half)123.4f }),
                    14 => Decimals(new decimal[] { decimal.MinValue, -2.5m, -1m, -0.5m, 0m, 0.5m, 1m, 1.5m, 2.5m, 123.456m, decimal.MaxValue }),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Integer<T>(T[] values) where T : IBinaryInteger<T>, IMinMaxValue<T>
                {
                    var hash = new Hasher();
                    foreach (var a in values)
                    {
                        Common(hash, a);
                        hash.Add(T.IsEvenInteger(a));
                        hash.Add(T.IsOddInteger(a));
                        hash.Add(T.IsPow2(a));
                        hash.Add(Code(() => T.Log2(a)));
                        hash.Add(Code(() => T.PopCount(a)));
                        hash.Add(Code(() => T.LeadingZeroCount(a)));
                        hash.Add(Code(() => T.TrailingZeroCount(a)));
                        hash.Add(Code(() => T.RotateLeft(a, 3)));
                        hash.Add(Code(() => T.RotateRight(a, 5)));
                        hash.Add(a.GetByteCount());
                        hash.Add(a.GetShortestBitLength());
                        Endian(hash, a);
                        foreach (var b in values)
                        {
                            Pair(hash, a, b);
                            hash.Add(Code(() => T.DivRem(a, b).Quotient));
                            hash.Add(Code(() => T.DivRem(a, b).Remainder));
                            hash.Add(Code(() => a / b));
                            hash.Add(Code(() => a % b));
                            hash.Add(Code(() => checked(a + b)));
                            hash.Add(Code(() => checked(a - b)));
                            hash.Add(Code(() => checked(a * b)));
                            hash.Add(Code(() => unchecked(a * b)));
                        }
                    }
                    return hash.Value;
                }

                private static int Floating<T>(T[] values) where T : IFloatingPointIeee754<T>, IMinMaxValue<T>
                {
                    var hash = new Hasher();
                    foreach (var a in values)
                    {
                        Common(hash, a);
                        hash.Add(T.IsNegativeInfinity(a));
                        hash.Add(T.IsPositiveInfinity(a));
                        hash.Add(Code(() => T.Ceiling(a)));
                        hash.Add(Code(() => T.Floor(a)));
                        hash.Add(Code(() => T.Round(a)));
                        hash.Add(Code(() => T.Round(a, MidpointRounding.AwayFromZero)));
                        hash.Add(Code(() => T.Round(a, MidpointRounding.ToZero)));
                        hash.Add(Code(() => T.Round(a, 1)));
                        hash.Add(Code(() => T.Round(a, 2)));
                        hash.Add(Code(() => T.Truncate(a)));
                        hash.Add(Code(() => T.BitIncrement(a)));
                        hash.Add(Code(() => T.BitDecrement(a)));
                        hash.Add(Code(() => T.ILogB(a)));
                        hash.Add(Code(() => T.ScaleB(a, 3)));
                        hash.Add(Code(() => T.ScaleB(a, -1000)));
                        hash.Add(Code(() => T.ConvertToInteger<int>(a)));
                        hash.Add(Code(() => T.ConvertToInteger<long>(a)));
                        hash.Add(a.GetExponentShortestBitLength());
                        hash.Add(a.GetSignificandBitLength());
                        foreach (var b in values)
                        {
                            Pair(hash, a, b);
                            hash.Add(Code(() => T.Ieee754Remainder(a, b)));
                            hash.Add(Code(() => T.FusedMultiplyAdd(a, b, T.One)));
                            hash.Add(Code(() => a % b));
                        }
                    }
                    return hash.Value;
                }

                private static int Decimals(decimal[] values)
                {
                    var hash = new Hasher();
                    foreach (var a in values)
                    {
                        Common(hash, a);
                        hash.Add(Code(() => decimal.Round(a, 1)));
                        hash.Add(Code(() => decimal.Round(a, MidpointRounding.AwayFromZero)));
                        hash.Add(Code(() => decimal.Ceiling(a)));
                        hash.Add(Code(() => decimal.Floor(a)));
                        hash.Add(Code(() => decimal.Truncate(a)));
                        foreach (var b in values)
                        {
                            Pair(hash, a, b);
                            hash.Add(Code(() => a % b));
                        }
                    }
                    return hash.Value;
                }

                private static void Common<T>(Hasher hash, T a) where T : INumber<T>, IMinMaxValue<T>
                {
                    hash.Add(T.IsNegative(a));
                    hash.Add(T.IsPositive(a));
                    hash.Add(T.IsZero(a));
                    hash.Add(T.IsNormal(a));
                    hash.Add(T.IsSubnormal(a));
                    hash.Add(T.IsFinite(a));
                    hash.Add(T.IsInfinity(a));
                    hash.Add(T.IsNaN(a));
                    hash.Add(T.IsInteger(a));
                    hash.Add(T.IsCanonical(a));
                    hash.Add(T.IsRealNumber(a));
                    hash.Add(Code(() => T.Abs(a)));
                    hash.Add(Code(() => T.Sign(a)));
                    Conversions(hash, a);
                }

                private static void Pair<T>(Hasher hash, T a, T b) where T : INumber<T>
                {
                    hash.Add(Code(() => T.MaxMagnitude(a, b)));
                    hash.Add(Code(() => T.MinMagnitude(a, b)));
                    hash.Add(Code(() => T.MaxMagnitudeNumber(a, b)));
                    hash.Add(Code(() => T.MinMagnitudeNumber(a, b)));
                    hash.Add(Code(() => T.Max(a, b)));
                    hash.Add(Code(() => T.Min(a, b)));
                    hash.Add(Code(() => T.MaxNumber(a, b)));
                    hash.Add(Code(() => T.MinNumber(a, b)));
                    hash.Add(Code(() => T.CopySign(a, b)));
                    hash.Add(Code(() => T.Clamp(a, T.Min(a, b), T.Max(a, b))));
                    // Integer CompareTo returns the difference on every platform;
                    // Half's depends on the hardware, so only its sign is compared.
                    hash.Add(Code(() => a is Half ? Math.Sign(a.CompareTo(b)) : a.CompareTo(b)));
                    hash.Add(a.Equals(b));
                }

                // Writes the value both ways and reads it back as the narrowest
                // and widest types, signed and unsigned, from exact, shorter and
                // longer spans.
                private static void Endian<T>(Hasher hash, T a) where T : IBinaryInteger<T>
                {
                    var bytes = new byte[a.GetByteCount()];
                    a.WriteBigEndian(bytes);
                    foreach (var value in bytes) hash.Add(value);
                    a.WriteLittleEndian(bytes);
                    foreach (var value in bytes) hash.Add(value);
                    var shorter = bytes.AsSpan(0, Math.Max(1, bytes.Length / 2)).ToArray();
                    var longer = new byte[bytes.Length + 3];
                    bytes.CopyTo(longer, 0);
                    foreach (var source in new[] { bytes, shorter, longer })
                    {
                        foreach (var isUnsigned in new[] { false, true })
                        {
                            hash.Add(Code(() => T.ReadBigEndian(source, isUnsigned)));
                            hash.Add(Code(() => T.ReadLittleEndian(source, isUnsigned)));
                            hash.Add(T.TryReadBigEndian(source, isUnsigned, out var big));
                            hash.Add(Code(() => big));
                            hash.Add(Code(() => ReadBig<int>(source, isUnsigned)));
                            hash.Add(Code(() => ReadLittle<long>(source, isUnsigned)));
                            hash.Add(Code(() => ReadBig<byte>(source, isUnsigned)));
                        }
                    }
                }

                private static void Conversions<T>(Hasher hash, T a) where T : INumberBase<T>
                {
                    hash.Add(Code(() => sbyte.CreateChecked(a)));
                    hash.Add(Code(() => sbyte.CreateSaturating(a)));
                    hash.Add(Code(() => sbyte.CreateTruncating(a)));
                    hash.Add(Code(() => byte.CreateChecked(a)));
                    hash.Add(Code(() => byte.CreateSaturating(a)));
                    hash.Add(Code(() => byte.CreateTruncating(a)));
                    hash.Add(Code(() => int.CreateChecked(a)));
                    hash.Add(Code(() => int.CreateSaturating(a)));
                    hash.Add(Code(() => int.CreateTruncating(a)));
                    hash.Add(Code(() => uint.CreateChecked(a)));
                    hash.Add(Code(() => uint.CreateSaturating(a)));
                    hash.Add(Code(() => uint.CreateTruncating(a)));
                    hash.Add(Code(() => long.CreateChecked(a)));
                    hash.Add(Code(() => long.CreateSaturating(a)));
                    hash.Add(Code(() => long.CreateTruncating(a)));
                    hash.Add(Code(() => ulong.CreateChecked(a)));
                    hash.Add(Code(() => ulong.CreateSaturating(a)));
                    hash.Add(Code(() => ulong.CreateTruncating(a)));
                    hash.Add(Code(() => Saturate<char, T>(a)));
                    hash.Add(Code(() => Int128.CreateSaturating(a)));
                    hash.Add(Code(() => UInt128.CreateTruncating(a)));
                    hash.Add(Code(() => float.CreateChecked(a)));
                    hash.Add(Code(() => double.CreateSaturating(a)));
                    hash.Add(Code(() => Half.CreateTruncating(a)));
                    hash.Add(Code(() => decimal.CreateSaturating(a)));
                }

                // Some types implement these members explicitly, so they are
                // reached as generic code reaches them.
                private static R ReadBig<R>(byte[] source, bool isUnsigned) where R : IBinaryInteger<R> =>
                    R.ReadBigEndian(source, isUnsigned);

                private static R ReadLittle<R>(byte[] source, bool isUnsigned) where R : IBinaryInteger<R> =>
                    R.ReadLittleEndian(source, isUnsigned);

                private static R Saturate<R, T>(T value) where R : INumberBase<R> where T : INumberBase<T> =>
                    R.CreateSaturating(value);

                // The result's value, or the kind of exception it threw. A
                // floating result hashes its bits, with every NaN alike.
                private static int Code<R>(Func<R> compute)
                {
                    try
                    {
                        return compute() switch
                        {
                            float value => float.IsNaN(value) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(value),
                            double value => double.IsNaN(value) ? 0x7ff8 : BitConverter.DoubleToInt64Bits(value).GetHashCode(),
                            Half value => Half.IsNaN(value) ? 0x7e00 : BitConverter.HalfToInt16Bits(value),
                            decimal value => decimal.GetBits(value)[0] ^ decimal.GetBits(value)[1] * 3 ^ decimal.GetBits(value)[2] * 7 ^ decimal.GetBits(value)[3] * 11,
                            // Int128's own hash code is randomized per process.
                            Int128 value => ((ulong)value).GetHashCode() ^ ((ulong)(value >> 64)).GetHashCode() * 7,
                            UInt128 value => ((ulong)value).GetHashCode() ^ ((ulong)(value >> 64)).GetHashCode() * 7,
                            var value => value!.GetHashCode(),
                        };
                    }
                    catch (OverflowException) { return -101; }
                    catch (DivideByZeroException) { return -102; }
                    catch (ArgumentOutOfRangeException) { return -103; }
                    catch (ArgumentException) { return -104; }
                    catch (NotSupportedException) { return -105; }
                    catch (Exception) { return -106; }
                }
            }
            """;
}
