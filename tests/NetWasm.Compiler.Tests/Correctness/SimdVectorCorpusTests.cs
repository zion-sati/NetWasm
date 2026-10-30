namespace NetWasm.Compiler.Tests.Correctness;

// Vector<T> and the System.Runtime.Intrinsics vectors are upstream's sources.
// Vector<T> is 16 bytes in NetWasm and follows the host on desktop, so results
// that depend on the width compare against Count rather than a constant, and
// floating-point reductions use values every summation order adds exactly.
[Collection(CorrectnessTestGroup.Name)]
public sealed class SimdVectorCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SimdVectorsMatchDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "SimdVectorCorpus",
            "NetWasm.Correctness.SimdVectors",
            """
            using System;
            using System.Numerics;
            using System.Runtime.Intrinsics;

            namespace NetWasm.Correctness.SimdVectors;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Reinterpret(),
                    1 => IntegerArithmetic(),
                    2 => FloatingArithmetic(),
                    3 => LoadsAndStores(),
                    4 => Conversions(),
                    5 => Comparisons(),
                    6 => NumericsVector(),
                    7 => OtherWidths(),
                    8 => Shuffles(),
                    9 => TooShort(),
                    11 => ScalarMinMax(),
                    12 => ScalarConversions(),
                    20 => Operations<byte>([0, 1, 127, 128, 255, 3, 200, 9, 16, 17, 99, 100, 254, 2, 64, 65],
                        [255, 1, 128, 127, 0, 5, 199, 9, 15, 18, 100, 99, 1, 254, 64, 66]),
                    21 => Operations<sbyte>([0, 1, 127, -128, -1, 3, -56, 9, 16, -17, 99, -100, 126, 2, -64, 65],
                        [-1, 1, -128, 127, 0, -5, -55, 9, 15, -18, -100, 99, 1, -126, -64, 66]),
                    22 => Operations<short>([0, -1, short.MaxValue, short.MinValue, 300, -300, 7, 1000],
                        [short.MinValue, -1, 1, short.MaxValue, -300, 300, 9, -1000]),
                    23 => Operations<ushort>([0, 1, ushort.MaxValue, 32768, 300, 65000, 7, 1000],
                        [ushort.MaxValue, 1, 1, 32767, 301, 64999, 9, 1000]),
                    24 => Operations<int>([0, -1, int.MaxValue, int.MinValue], [int.MinValue, -1, 3, int.MaxValue]),
                    25 => Operations<uint>([0, 1, uint.MaxValue, 0x80000000], [uint.MaxValue, 1, 3, 0x7FFFFFFF]),
                    26 => Operations<long>([long.MinValue, -5], [long.MaxValue, -5]) ^
                        Operations<long>([long.MaxValue, 0], [-1, 0]),
                    27 => Operations<ulong>([ulong.MaxValue, 0x8000000000000000], [1, 0x7FFFFFFFFFFFFFFF]) ^
                        Operations<ulong>([0, 7], [ulong.MaxValue, 7]),
                    28 => Operations<float>([1.5f, -0f, float.NaN, float.PositiveInfinity],
                        [-2.25f, 0f, 1f, float.NegativeInfinity]),
                    29 => Operations<double>([-0.0, double.NaN], [0.0, 2.5]) ^
                        Operations<double>([double.MaxValue, -1e-300], [double.NegativeInfinity, 1e-300]),
                    30 => NativeLanes((nint)(-7), 5) ^ NativeLanes(-100000, 3000),
                    31 => NativeLanes((nuint)7, 5) ^ NativeLanes((nuint)0x40000000, 3),
                    32 => Unsupported(),
                    33 => Elements(),
                    34 => Division(),
                    35 => Roots(),
                    36 => Sums(),
                    37 => Rounding(),
                    38 => ConvertingLanes(),
                    39 => Resizing(),
                    _ => Element(input),
                };

                // As* reinterprets bits; it does not convert each element.
                private static int Reinterpret()
                {
                    var bytes = Vector.AsVectorByte(new Vector<int>(0x01020304));
                    var lanes = Vector128.Create(0x3F800000u).AsSingle();
                    return bytes[0] * 1000 + bytes[1] * 100 + bytes[2] * 10 + bytes[3] +
                        (lanes.GetElement(3) == 1f ? 10000 : 0) +
                        (Vector128.Create((short)-1).AsByte().GetElement(15) == 255 ? 20000 : 0);
                }

                private static int IntegerArithmetic()
                {
                    var a = Vector128.Create(1, -2, 3, int.MaxValue);
                    var b = Vector128.Create(5, 6, -7, 1);
                    var sum = a + b;
                    var product = a * b;
                    var bits = (a & b) | (a ^ ~b);
                    var shifted = Vector128.ShiftLeft(a, 3) + Vector128.ShiftRightArithmetic(a, 1) +
                        Vector128.ShiftRightLogical(a, 2);
                    var bytes = Vector128.Create((byte)250) + Vector128.Create((byte)10);
                    return Mix(sum, product, bits, shifted, bytes, -a, Vector128.Abs(a),
                        Vector128.Min(a, b)) ^ Vector128.Sum(Vector128.Create(1, 2, 3, 4)) ^
                        (int)Vector128.Dot(Vector128.Create(1L, 2L), Vector128.Create(3L, 4L));
                }

                private static int FloatingArithmetic()
                {
                    var a = Vector128.Create(1.5f, -2.25f, 3f, 16f);
                    var b = Vector128.Create(0.5f, 4f, -1f, 2f);
                    var mixed = Vector128.Max(a, b) * Vector128.Min(a, b) - a / b;
                    var roots = Vector128.Sqrt(Vector128.Abs(a));
                    var nan = Vector128.Max(Vector128.Create(float.NaN), a);
                    var d = Vector128.Create(2.5, -3.5);
                    return Mix(mixed, roots, Vector128.Floor(a), Vector128.Ceiling(-a),
                        float.IsNaN(nan.GetElement(0)), d * d, Vector128.Sum(Vector128.Create(0.5f, 0.25f, 1f, 2f)));
                }

                private static int LoadsAndStores()
                {
                    Span<int> source = [9, 8, 7, 6, 5, 4, 3, 2];
                    var first = Vector128.Create<int>(source);
                    var second = Vector128.LoadUnsafe(ref source[0], 4);
                    var destination = new int[6];
                    (first + second).CopyTo(destination, 1);
                    var array = new float[] { 1, 2, 3, 4, 5 };
                    var fromArray = Vector128.Create(array, 1);
                    return Mix(destination[0], destination[1], destination[4], destination[5],
                        fromArray, first.ToScalar(), Vector128.Create(7).WithElement(2, 1));
                }

                private static int Conversions()
                {
                    var ints = Vector128.Create(-3, 7, int.MinValue, 100);
                    var floats = Vector128.ConvertToSingle(ints);
                    var back = Vector128.ConvertToInt32(Vector128.Create(1.9f, -1.9f, 2.5f, -0.5f));
                    var (lower, upper) = Vector128.Widen(Vector128.Create((short)-1, 2, 3, 4, 5, 6, 7, short.MinValue));
                    var narrowed = Vector128.Narrow(Vector128.Create(1, 2, 3, 0x10004), Vector128.Create(5, 6, 7, -1));
                    return Mix(floats, back, lower, upper, narrowed,
                        Vector128.ConvertToDouble(Vector128.Create(3L, -4L)));
                }

                private static int Comparisons()
                {
                    var a = Vector128.Create(1, 5, -3, 8);
                    var b = Vector128.Create(2, 5, -4, 9);
                    var mask = Vector128.GreaterThan(a, b) | Vector128.Equals(a, b);
                    return (int)Vector128.ExtractMostSignificantBits(mask) * 100 +
                        (Vector128.EqualsAll(a, a) ? 1 : 0) + (Vector128.EqualsAny(a, b) ? 2 : 0) +
                        (Vector128.GreaterThanAll(a, b) ? 4 : 0) + (Vector128.LessThanAny(a, b) ? 8 : 0) +
                        (int)Vector128.ExtractMostSignificantBits(Vector128.ConditionalSelect(mask, a, b).AsSByte()) * 10000 +
                        (int)Vector128.ExtractMostSignificantBits(Vector128.Create(-1f, 2f, -0f, float.NaN));
                }

                private static int NumericsVector()
                {
                    var ones = Vector<int>.One;
                    var indices = Vector<int>.Indices;
                    var sum = Vector.Sum(ones) == Vector<int>.Count ? 1 : 0;
                    var dot = Vector.Dot(indices, ones) == Vector<int>.Count * (Vector<int>.Count - 1) / 2 ? 2 : 0;
                    var select = Vector.ConditionalSelect(Vector.GreaterThan(indices, new Vector<int>(1)), ones, -ones);
                    Vector.Widen(new Vector<short>(-2), out var widened, out var high);
                    return sum + dot + (select[0] == -1 && select[2] == 1 ? 4 : 0) +
                        (widened[0] == -2 && high[0] == -2 ? 8 : 0) +
                        (Vector.EqualsAll(Vector.Abs(-ones), ones) ? 16 : 0) +
                        (Vector<byte>.Count == Vector<int>.Count * 4 ? 32 : 0) +
                        (Vector.AsVectorSingle(new Vector<int>(0x40000000))[0] == 2f ? 64 : 0);
                }

                private static int OtherWidths()
                {
                    var wide = Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8) * 3;
                    var narrow = Vector64.Create((short)4, 3, 2, 1);
                    var wider = Vector512.Create(2L) + Vector512<long>.One;
                    return Mix(wide.GetLower(), wide.GetUpper(), narrow + narrow,
                        Vector64.Sum(narrow), wider.GetElement(7), Vector256.ExtractMostSignificantBits(-wide));
                }

                private static int Shuffles()
                {
                    var bytes = Vector128.Create((byte)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
                    var reversed = Vector128.Shuffle(bytes, Vector128.Create((byte)15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0));
                    var outOfRange = Vector128.Shuffle(Vector128.Create(1, 2, 3, 4), Vector128.Create(3, 0, 9, -1));
                    return Mix(reversed, outOfRange, Vector128.Create(5, 6, 7, 8).GetUpper());
                }

                private static int TooShort()
                {
                    var result = 0;
                    try { _ = Vector128.Create<int>(new int[3]); }
                    catch (ArgumentOutOfRangeException) { result |= 1; }
                    try { Vector128.Create(1, 2, 3, 4).CopyTo(new int[2]); }
                    catch (ArgumentException) { result |= 2; }
                    try { _ = Vector128.Create(1).GetElement(4); }
                    catch (ArgumentOutOfRangeException) { result |= 4; }
                    try { _ = new Vector<int>(new int[1]); }
                    catch (IndexOutOfRangeException) { result |= 8; }
                    catch (ArgumentOutOfRangeException) { result |= 16; }
                    return result;
                }

                // The vector paths use the primitives' generic math, which follows
                // IEEE 754:2019: NaN propagates and -0 orders below +0.
                private static int ScalarMinMax() =>
                    (double.IsNaN(double.Max(double.NaN, 1)) ? 1 : 0) |
                    (float.IsNaN(float.Min(2f, float.NaN)) ? 2 : 0) |
                    (double.IsNegative(double.Min(0.0, -0.0)) ? 4 : 0) |
                    (!float.IsNegative(float.Max(-0f, 0f)) ? 8 : 0) |
                    (!double.IsNegative(double.Clamp(-0.0, 0.0, 1.0)) ? 16 : 0) |
                    (float.IsNaN(float.Clamp(float.NaN, 0f, 1f)) ? 32 : 0) |
                    (double.Clamp(5, 1, 3) == 3 ? 64 : 0);

                // The vector conversions use the primitives' generic conversions,
                // which truncate toward zero, saturate or throw by kind.
                private static int ScalarConversions()
                {
                    var result = (int.CreateSaturating(-1.9f) == -1 ? 1 : 0) |
                        (int.CreateChecked(-2.5) == -2 ? 2 : 0) |
                        (long.CreateSaturating(-3e30) == long.MinValue ? 4 : 0) |
                        (short.CreateSaturating(-40000.5f) == short.MinValue ? 8 : 0) |
                        (int.CreateTruncating(-7.9) == -7 ? 16 : 0) |
                        (uint.CreateSaturating(-1.5f) == 0 ? 32 : 0) |
                        (float.ConvertToInteger<int>(-3e9f) == int.MinValue ? 64 : 0) |
                        (int.CreateSaturating(1e20) == int.MaxValue ? 128 : 0) |
                        (ushort.CreateTruncating(70000) == 4464 ? 256 : 0) |
                        (Truncate<char, int>(-1) == '\uffff' ? 512 : 0) |
                        (nint.CreateSaturating(-1e300) == nint.MinValue ? 1024 : 0) |
                        (ulong.CreateTruncating(-1L) == ulong.MaxValue ? 2048 : 0) |
                        (short.CreateTruncating(98304.75) == short.MinValue ? 4096 : 0);
                    try { _ = int.CreateChecked(3e9); }
                    catch (OverflowException) { result |= 8192; }
                    try { _ = uint.CreateChecked(-1); }
                    catch (OverflowException) { result |= 16384; }
                    return result;
                }

                // Every lowered operation on one element type. The scalar and
                // mixed overloads beside them keep their software implementations.
                private static int Operations<T>(T[] left, T[] right)
                {
                    var a = Vector128.Create<T>(left);
                    var b = Vector128.Create<T>(right);
                    var mask = Vector128.GreaterThan(a, b);
                    var hash = Mix(a + b, a - b, a * b, a & b, a | b, a ^ b, ~a, -a,
                        Vector128.AndNot(a, b), Vector128.Abs(a), Vector128.Min(a, b), Vector128.Max(a, b),
                        Vector128.Equals(a, b), mask, Vector128.GreaterThanOrEqual(a, b),
                        Vector128.LessThan(a, b), Vector128.LessThanOrEqual(a, b),
                        Vector128.ConditionalSelect(mask, a, b), a << 3, a >> 5, a >>> 7, a << 33,
                        Vector128.Create(left[1]), Vector128.Add(a, b), Vector128.Multiply(b, b),
                        Vector128.Negate(b), Vector128.OnesComplement(b), Vector128.Xor(a, b),
                        Vector128.BitwiseAnd(a, b), Vector128.BitwiseOr(a, b), Vector128.Subtract(b, a));
                    var stored = new T[right.Length + 1];
                    Vector128.StoreUnsafe(a + b, ref stored[0]);
                    (a - b).StoreUnsafe(ref stored[0], 1);
                    hash = unchecked(hash * 31 + Mix(Vector128.LoadUnsafe(ref left[0]), Vector128.LoadUnsafe(ref right[0], 0),
                        Vector128.LoadUnsafe(ref stored[0]), Vector128.LoadUnsafe(ref stored[0], 1), a.ToScalar()!, b.ToScalar()!,
                        (Vector.LoadUnsafe(ref stored[0]) - Vector.LoadUnsafe(ref left[0]))[0]!));
                    var numerics = new Vector<T>(left[0]) + Vector.Create(left[2]);
                    return unchecked(hash * 31 + (int)Vector128.ExtractMostSignificantBits(a) * 3 +
                        (int)Vector128.ExtractMostSignificantBits(mask) * 5 +
                        (a == b ? 7 : 0) + (a != b ? 11 : 0) + (a == Vector128.Create<T>(left) ? 13 : 0) +
                        (Vector128.EqualsAny(a, b) ? 17 : 0) + (Vector128.EqualsAll(a, b) ? 19 : 0) +
                        (Vector.EqualsAll(numerics, numerics + Vector<T>.Zero) ? 23 : 0) + Lanes(numerics[0]!));
                }

                // nint and nuint lanes follow the pointer width, so every lane holds
                // the same value, results fit in 32 bits and only lane 0 and the
                // lane mask count.
                private static int NativeLanes(nint left, nint right)
                {
                    var a = Vector128.Create(left);
                    var b = Vector128.Create(right);
                    var all = (1u << Vector128<nint>.Count) - 1;
                    var mask = Vector128.LessThan(a, b);
                    return Mix((long)(a + b)[0], (long)(a - b)[0], (long)(a * b)[0], (long)(a ^ b)[0],
                        (long)(-a)[0], (long)Vector128.Abs(a)[0], (long)Vector128.Min(a, b)[0],
                        (long)Vector128.Max(a, b)[0], (long)(a >> 3)[0], (long)(b >>> 2)[0], (long)(a << 66)[0],
                        Vector128.ExtractMostSignificantBits(mask) == all, Vector128.ExtractMostSignificantBits(a) == all,
                        (long)Vector128.ConditionalSelect(mask, a, b)[0], a == b, a != b);
                }

                private static int NativeLanes(nuint left, nuint right)
                {
                    var a = Vector128.Create(left);
                    var b = Vector128.Create(right);
                    var all = (1u << Vector128<nuint>.Count) - 1;
                    var mask = Vector128.GreaterThan(a, b);
                    return Mix((ulong)(a + b)[0], (ulong)(a - b)[0], (ulong)(a * b)[0], (ulong)(a | b)[0],
                        (ulong)Vector128.Min(a, b)[0], (ulong)Vector128.Max(a, b)[0], (ulong)(a >> 3)[0],
                        (ulong)(a >>> 2)[0], (ulong)(b << 65)[0], Vector128.ExtractMostSignificantBits(mask) == all,
                        (ulong)Vector128.ConditionalSelect(mask, a, b)[0], a == b, a != b);
                }

                // The per-lane Create overloads fill lanes in order.
                private static int Elements() => Mix(
                    Vector128.Create((byte)1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 255),
                    Vector128.Create((sbyte)-1, 2, -3, 4, -5, 6, -7, 8, -9, 10, -11, 12, -13, 14, -15, -128),
                    Vector128.Create((short)-1, 2, -3, 4, -5, 6, -7, short.MinValue),
                    Vector128.Create((ushort)1, 2, 3, 4, 5, 6, 7, ushort.MaxValue),
                    Vector128.Create(-1, 2, int.MinValue, 4), Vector128.Create(1u, 2u, uint.MaxValue, 4u),
                    Vector128.Create(long.MinValue, 2L), Vector128.Create(ulong.MaxValue, 2UL),
                    Vector128.Create(-0f, 2.5f, float.NaN, float.NegativeInfinity), Vector128.Create(-0.0, 1e300),
                    Vector128.Create((byte)7), Vector128.Create((short)-7), Vector128.Create(7L), Vector128.Create(7.5),
                    (long)Vector128.Create((nint)(-3)).GetElement(0), (ulong)Vector128.Create((nuint)3).GetElement(0));

                // Integer lanes divide one by one: a zero divisor throws, and so does
                // MinValue / -1 where the quotient overflows int or long.
                private static int Division() => Mix(
                    Quotients<byte>([200, 7, 255, 0, 9, 100, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 3),
                    Quotients<sbyte>([-128, 7, 127, 0, -9, 100, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], -1),
                    Quotients<short>([short.MinValue, 7, -300, 0, 9, 1000, 1, 2], -1),
                    Quotients<ushort>([65535, 7, 300, 0, 9, 1000, 1, 2], 7),
                    Quotients<int>([int.MinValue, -7, 300, 0], -1), Quotients<int>([int.MaxValue, -7, 300, 0], 7),
                    Quotients<uint>([uint.MaxValue, 7, 300, 0], 2),
                    Quotients<long>([long.MinValue, -7], -1), Quotients<long>([long.MaxValue, -7], 3),
                    Quotients<ulong>([ulong.MaxValue, 7], 5),
                    Vector128.Create(7.5f, -0f, 1f, float.PositiveInfinity) / Vector128.Create(2f, 3f, 0f, -1f),
                    Vector128.Create(1.0, -1e300) / 0.5, Vector.Divide(new Vector<float>(9f), 3f)[0],
                    Vector128.Create(10, 20, 30, 40) / Vector128.Create(3, -4, 5, -6),
                    Vector128.Create((byte)200) / Vector128.Create((byte)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15).WithElement(0, (byte)1));

                private static int Quotients<T>(T[] left, T right) where T : INumber<T>
                {
                    var a = Vector128.Create<T>(left);
                    var result = 0;
                    try { result = Mix(a / right, Vector128.Divide(a, Vector128.Create(right))); }
                    catch (DivideByZeroException) { result = 1; }
                    catch (OverflowException) { result = 2; }
                    try { result = Mix(result, a / Vector128.Create(T.One).WithElement(Vector128<T>.Count - 1, T.Zero)); }
                    catch (DivideByZeroException) { result ^= 4; }
                    catch (OverflowException) { result ^= 8; }
                    try { result = Mix(result, a / a); }
                    catch (DivideByZeroException) { result ^= 16; }
                    catch (OverflowException) { result ^= 32; }
                    return result;
                }

                // Integer lanes take the root as float or double and truncate it
                // back with saturation, so a negative lane's NaN becomes 0.
                private static int Roots() => Mix(
                    Vector128.Sqrt(Vector128.Create((byte)0, 1, 2, 3, 4, 15, 16, 17, 99, 100, 120, 200, 224, 225, 254, 255)),
                    Vector128.Sqrt(Vector128.Create((sbyte)-1, 1, 2, 3, 4, 15, 16, 17, 99, 100, 120, -128, 124, 125, 126, 127)),
                    Vector128.Sqrt(Vector128.Create((short)-1, 0, 2, 3, short.MaxValue, 15, 16, short.MinValue)),
                    Vector128.Sqrt(Vector128.Create((ushort)65535, 0, 2, 3, 65024, 15, 16, 17)),
                    Vector128.Sqrt(Vector128.Create(-4, int.MaxValue, 2147395600, 99)),
                    Vector128.Sqrt(Vector128.Create(uint.MaxValue, 0u, 4294836225u, 99u)),
                    Vector128.Sqrt(Vector128.Create(-9L, long.MaxValue)), Vector128.Sqrt(Vector128.Create(ulong.MaxValue, 99UL)),
                    Vector128.Sqrt(Vector128.Create(2f, -0f, float.PositiveInfinity, -1f)),
                    Vector128.Sqrt(Vector128.Create(2.0, 1e-310)), Vector.SquareRoot(new Vector<double>(49.0))[0]);

                // Floating lanes sum as the halves' sums, the order of the hardware's
                // horizontal adds, which keeps a sum of negative zeros negative. The
                // signs count separately: two flipped sign bits cancel in Mix.
                private static int Sums() => Mix(
                    (float.IsNegative(Vector128.Sum(Vector128.Create(-0f))) ? 1 : 0) +
                        (double.IsNegative(Vector128.Sum(Vector128.Create(-0.0))) ? 2 : 0),
                    Vector128.Sum(Vector128.Create(1e8f, 1f, -1e8f, 1f)), Vector128.Sum(Vector128.Create(1e16, 1.0)),
                    Vector128.Sum(Vector128.Create(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue)),
                    Vector128.Sum(Vector128.Create((byte)200)), Vector128.Sum(Vector128.Create((sbyte)-100)),
                    Vector128.Sum(Vector128.Create((short)30000)), Vector128.Sum(Vector128.Create((ushort)40000)),
                    Vector128.Sum(Vector128.Create(int.MaxValue)), Vector128.Sum(Vector128.Create(uint.MaxValue)),
                    Vector128.Sum(Vector128.Create(long.MinValue, -1L)), Vector128.Sum(Vector128.Create(ulong.MaxValue, 2UL)),
                    Vector128.Sum(Vector128.Create((nint)(-3))) == -3 * Vector128<nint>.Count,
                    Vector.Sum(new Vector<int>(5)) == 5 * Vector<int>.Count,
                    Vector128.Dot(Vector128.Create(1e8f, 1f, -1e8f, 3f), Vector128.Create(1f, 1f, 1f, 2f)),
                    Vector128.Dot(Vector128.Create(0.1, 0.2), Vector128.Create(0.3, 0.4)),
                    Vector128.Dot(Vector128.Create((byte)16), Vector128.Create((byte)16)));

                // Midpoints round to even, as Math.Round does by default.
                private static int Rounding()
                {
                    var f = Vector128.Create(-0.5f, 0.5f, 1.5f, -2.5f);
                    var g = Vector128.Create(float.NaN, -0f, float.NegativeInfinity, 8388609.5f);
                    var d = Vector128.Create(-1.5, 2.5);
                    var e = Vector128.Create(-0.0, 4503599627370497.0);
                    return Mix(Vector128.Floor(f), Vector128.Ceiling(f), Vector128.Round(f), Vector128.Truncate(f),
                        Vector128.Floor(g), Vector128.Ceiling(g), Vector128.Round(g), Vector128.Truncate(g),
                        Vector128.Floor(d), Vector128.Ceiling(d), Vector128.Round(d), Vector128.Truncate(d),
                        Vector128.Floor(e), Vector128.Ceiling(e), Vector128.Round(e), Vector128.Truncate(e),
                        Vector.Floor(new Vector<double>(-2.5))[0], Vector.Ceiling(new Vector<float>(-2.5f))[0]);
                }

                // Floating values truncate toward zero and saturate, NaN becoming 0.
                private static int ConvertingLanes() => Mix(
                    Vector128.ConvertToInt32(Vector128.Create(float.NaN, 3e9f, -3e9f, -1.9f)),
                    Vector128.ConvertToUInt32(Vector128.Create(float.NaN, 5e9f, -1f, 3e9f)),
                    Vector128.ConvertToInt64(Vector128.Create(double.NaN, -1e19)), Vector128.ConvertToInt64(Vector128.Create(1e19, -7.9)),
                    Vector128.ConvertToUInt64(Vector128.Create(-1.0, 2e19)), Vector128.ConvertToUInt64(Vector128.Create(double.NaN, 1.8e19)),
                    Vector128.ConvertToSingle(Vector128.Create(16777217, -16777217, int.MaxValue, int.MinValue)),
                    Vector128.ConvertToSingle(Vector128.Create(uint.MaxValue, 16777217u, 2147483649u, 0u)),
                    Vector128.ConvertToDouble(Vector128.Create(long.MaxValue, 9007199254740993L)),
                    Vector128.ConvertToDouble(Vector128.Create(ulong.MaxValue, 9007199254740993UL)),
                    Vector128.ConvertToInt32Native(Vector128.Create(1.9f, -1.9f, 2f, 0f)),
                    Vector128.ConvertToUInt64Native(Vector128.Create(7.9, 0.5)),
                    Vector.ConvertToInt32(new Vector<float>(-7.5f))[0], Vector.ConvertToDouble(new Vector<long>(-3L))[0]);

                // Widen extends each half; Narrow truncates each lane.
                private static int Resizing()
                {
                    var bytes = Vector128.Create((byte)0, 1, 127, 128, 255, 3, 200, 9, 16, 17, 99, 100, 254, 2, 64, 65);
                    var shorts = Vector128.Create((short)-1, 2, short.MinValue, 4, 300, -300, short.MaxValue, 8);
                    var ints = Vector128.Create(-1, 0x10004, int.MinValue, int.MaxValue);
                    var longs = Vector128.Create(-1L, 0x100000004L);
                    var floats = Vector128.Create(1.5f, -0f, float.NaN, float.MaxValue);
                    return Mix(Vector128.WidenLower(bytes), Vector128.WidenUpper(bytes),
                        Vector128.WidenLower(bytes.AsSByte()), Vector128.WidenUpper(bytes.AsSByte()),
                        Vector128.WidenLower(shorts), Vector128.WidenUpper(shorts),
                        Vector128.WidenLower(shorts.AsUInt16()), Vector128.WidenUpper(shorts.AsUInt16()),
                        Vector128.WidenLower(ints), Vector128.WidenUpper(ints),
                        Vector128.WidenLower(ints.AsUInt32()), Vector128.WidenUpper(ints.AsUInt32()),
                        Vector128.WidenLower(floats), Vector128.WidenUpper(floats),
                        Vector128.Narrow(shorts, shorts.WithElement(0, (short)511)),
                        Vector128.Narrow(shorts.AsUInt16(), shorts.AsUInt16()),
                        Vector128.Narrow(ints, ints.WithElement(3, 0x7FFF8001)), Vector128.Narrow(ints.AsUInt32(), ints.AsUInt32()),
                        Vector128.Narrow(longs, longs.WithElement(1, long.MinValue)), Vector128.Narrow(longs.AsUInt64(), longs.AsUInt64()),
                        Vector128.Narrow(Vector128.Create(1e300, -0.1), Vector128.Create(double.NaN, 3.4028235677973366e38)),
                        Vector.Narrow(new Vector<int>(0x12345), new Vector<int>(-2))[0], Vector.WidenUpper(new Vector<byte>(250))[0]);
                }

                private static int Unsupported()
                {
                    var result = 0;
                    try { _ = Vector128.Create('a') + Vector128.Create('b'); }
                    catch (NotSupportedException) { result |= 1; }
                    try { _ = Vector128.Create(1m); }
                    catch (NotSupportedException) { result |= 2; }
                    try { _ = Vector128.Equals(Vector128<bool>.Zero, Vector128<bool>.Zero); }
                    catch (NotSupportedException) { result |= 4; }
                    return result;
                }

                private static TTo Truncate<TTo, TFrom>(TFrom value)
                    where TTo : INumberBase<TTo> where TFrom : INumberBase<TFrom> =>
                    TTo.CreateTruncating(value);

                private static int Element(int input) =>
                    Vector128.Create(10, 20, 30, 40).GetElement(input & 3) + Vector128<int>.Count;

                // HashCode is seeded per process, so results mix lanes deterministically.
                private static int Mix(params object[] values)
                {
                    var hash = 17;
                    foreach (var value in values) hash = unchecked(hash * 31 + Lanes(value));
                    return hash;
                }

                private static int Lanes(object value) => value switch
                {
                    // Bits distinguish -0 from +0; NaN payloads are not portable.
                    float f => float.IsNaN(f) ? 1 : BitConverter.SingleToInt32Bits(f),
                    double d => double.IsNaN(d) ? 1 : BitConverter.DoubleToInt64Bits(d).GetHashCode(),
                    Vector128<float> v => Mix(v.GetElement(0), v.GetElement(1), v.GetElement(2), v.GetElement(3)),
                    Vector128<double> v => Mix(v.GetElement(0), v.GetElement(1)),
                    Vector128<ulong> v => Sum(v), Vector128<nint> v => Sum(v), Vector128<nuint> v => Sum(v),
                    Vector128<byte> v => Sum(v), Vector128<short> v => Sum(v), Vector128<ushort> v => Sum(v),
                    Vector128<int> v => Sum(v), Vector128<uint> v => Sum(v), Vector128<long> v => Sum(v),
                    Vector128<sbyte> v => Sum(v),
                    Vector64<short> v => Sum(v.ToVector128()), Vector64<int> v => Sum(v.ToVector128()),
                    _ => value.GetHashCode(),
                };

                private static int Sum<T>(Vector128<T> vector)
                {
                    var hash = 0;
                    for (var index = 0; index < Vector128<T>.Count; index++)
                        hash = unchecked(hash * 7 + vector.GetElement(index)!.GetHashCode());
                    return hash;
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33,
                34, 35, 36, 37, 38, 39])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
