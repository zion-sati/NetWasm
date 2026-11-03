using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Every PackedSimd overload whose lanes are signed or unsigned computes what
// its element type's own arithmetic does, lane by lane: ordered comparisons,
// minimum and maximum, shifts, widening and narrowing, saturating and rounding
// arithmetic, lane reads, conversions and widening loads. The values sit at
// both ends of every integer width and around each sign bit, where a signed
// instruction standing in for an unsigned one, or the reverse, differs. Wasm
// has no unsigned 64-bit lane comparison, so ulong and wasm64's nuint compare
// with the sign bit flipped.
public sealed class PackedSimdLaneSemanticsTests
{
    // The checks' names, in the order Run makes them; generated with the checks
    // from upstream's PackedSimd.cs by eng/packedsimd/generate_packedsimd_lane_checks.py.
    private static readonly string[] Checks = ["ExtractScalar(sbyte, 0)", "ExtractScalar(sbyte, 15)", "ExtractScalar(byte, 0)", "ExtractScalar(byte, 15)", "ExtractScalar(short, 0)", "ExtractScalar(short, 7)", "ExtractScalar(ushort, 0)", "ExtractScalar(ushort, 7)", "ExtractScalar(int, 0)", "ExtractScalar(int, 3)", "ExtractScalar(uint, 0)", "ExtractScalar(uint, 3)", "ExtractScalar(long, 0)", "ExtractScalar(long, 1)", "ExtractScalar(ulong, 0)", "ExtractScalar(ulong, 1)", "ExtractScalar(nint, 0)", "ExtractScalar(nint, 1)", "ExtractScalar(nuint, 0)", "ExtractScalar(nuint, 1)", "MultiplyWideningLower(sbyte)", "MultiplyWideningLower(byte)", "MultiplyWideningLower(short)", "MultiplyWideningLower(ushort)", "MultiplyWideningLower(int)", "MultiplyWideningLower(uint)", "MultiplyWideningUpper(sbyte)", "MultiplyWideningUpper(byte)", "MultiplyWideningUpper(short)", "MultiplyWideningUpper(ushort)", "MultiplyWideningUpper(int)", "MultiplyWideningUpper(uint)", "AddPairwiseWidening(sbyte)", "AddPairwiseWidening(byte)", "AddPairwiseWidening(short)", "AddPairwiseWidening(ushort)", "AddSaturate(sbyte)", "AddSaturate(byte)", "AddSaturate(short)", "AddSaturate(ushort)", "SubtractSaturate(sbyte)", "SubtractSaturate(byte)", "SubtractSaturate(short)", "SubtractSaturate(ushort)", "Min(sbyte)", "Min(byte)", "Min(short)", "Min(ushort)", "Min(int)", "Min(uint)", "Max(sbyte)", "Max(byte)", "Max(short)", "Max(ushort)", "Max(int)", "Max(uint)", "AverageRounded(byte)", "AverageRounded(ushort)", "ShiftLeft(sbyte)", "ShiftLeft(byte)", "ShiftLeft(short)", "ShiftLeft(ushort)", "ShiftLeft(int)", "ShiftLeft(uint)", "ShiftLeft(long)", "ShiftLeft(ulong)", "ShiftLeft(nint)", "ShiftLeft(nuint)", "ShiftRightArithmetic(sbyte)", "ShiftRightArithmetic(byte)", "ShiftRightArithmetic(short)", "ShiftRightArithmetic(ushort)", "ShiftRightArithmetic(int)", "ShiftRightArithmetic(uint)", "ShiftRightArithmetic(long)", "ShiftRightArithmetic(ulong)", "ShiftRightArithmetic(nint)", "ShiftRightArithmetic(nuint)", "ShiftRightLogical(sbyte)", "ShiftRightLogical(byte)", "ShiftRightLogical(short)", "ShiftRightLogical(ushort)", "ShiftRightLogical(int)", "ShiftRightLogical(uint)", "ShiftRightLogical(long)", "ShiftRightLogical(ulong)", "ShiftRightLogical(nint)", "ShiftRightLogical(nuint)", "CompareLessThan(sbyte)", "CompareLessThan(byte)", "CompareLessThan(short)", "CompareLessThan(ushort)", "CompareLessThan(int)", "CompareLessThan(uint)", "CompareLessThan(long)", "CompareLessThan(ulong)", "CompareLessThan(nint)", "CompareLessThan(nuint)", "CompareLessThanOrEqual(sbyte)", "CompareLessThanOrEqual(byte)", "CompareLessThanOrEqual(short)", "CompareLessThanOrEqual(ushort)", "CompareLessThanOrEqual(int)", "CompareLessThanOrEqual(uint)", "CompareLessThanOrEqual(long)", "CompareLessThanOrEqual(ulong)", "CompareLessThanOrEqual(nint)", "CompareLessThanOrEqual(nuint)", "CompareGreaterThan(sbyte)", "CompareGreaterThan(byte)", "CompareGreaterThan(short)", "CompareGreaterThan(ushort)", "CompareGreaterThan(int)", "CompareGreaterThan(uint)", "CompareGreaterThan(long)", "CompareGreaterThan(ulong)", "CompareGreaterThan(nint)", "CompareGreaterThan(nuint)", "CompareGreaterThanOrEqual(sbyte)", "CompareGreaterThanOrEqual(byte)", "CompareGreaterThanOrEqual(short)", "CompareGreaterThanOrEqual(ushort)", "CompareGreaterThanOrEqual(int)", "CompareGreaterThanOrEqual(uint)", "CompareGreaterThanOrEqual(long)", "CompareGreaterThanOrEqual(ulong)", "CompareGreaterThanOrEqual(nint)", "CompareGreaterThanOrEqual(nuint)", "LoadWideningVector128(sbyte)", "LoadWideningVector128(byte)", "LoadWideningVector128(short)", "LoadWideningVector128(ushort)", "LoadWideningVector128(int)", "LoadWideningVector128(uint)", "ConvertToSingle(int)", "ConvertToSingle(uint)", "ConvertToDoubleLower(int)", "ConvertToDoubleLower(uint)", "ConvertToInt32Saturate(float)", "ConvertToUInt32Saturate(float)", "ConvertToInt32Saturate(double)", "ConvertToUInt32Saturate(double)", "ConvertNarrowingSaturateSigned(short)", "ConvertNarrowingSaturateSigned(int)", "ConvertNarrowingSaturateUnsigned(short)", "ConvertNarrowingSaturateUnsigned(int)", "SignExtendWideningLower(sbyte)", "SignExtendWideningLower(byte)", "SignExtendWideningLower(short)", "SignExtendWideningLower(ushort)", "SignExtendWideningLower(int)", "SignExtendWideningLower(uint)", "SignExtendWideningUpper(sbyte)", "SignExtendWideningUpper(byte)", "SignExtendWideningUpper(short)", "SignExtendWideningUpper(ushort)", "SignExtendWideningUpper(int)", "SignExtendWideningUpper(uint)", "ZeroExtendWideningLower(sbyte)", "ZeroExtendWideningLower(byte)", "ZeroExtendWideningLower(short)", "ZeroExtendWideningLower(ushort)", "ZeroExtendWideningLower(int)", "ZeroExtendWideningLower(uint)", "ZeroExtendWideningUpper(sbyte)", "ZeroExtendWideningUpper(byte)", "ZeroExtendWideningUpper(short)", "ZeroExtendWideningUpper(ushort)", "ZeroExtendWideningUpper(int)", "ZeroExtendWideningUpper(uint)"];

    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;
        using System.Runtime.Intrinsics.Wasm;

        public static unsafe class EntryPoint
        {
            private static readonly long[] s_patterns =
            [
                long.MaxValue, long.MinValue, 0, 1, -1, 2, -2, 3, 100, -100,
                0x7F, 0x80, 0xFF, 0x100, -0x80, -0x81,
                0x7FFF, 0x8000, 0xFFFF, -0x8000, -0x8001,
                0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF, -0x8000_0000L, -0x8000_0001L,
                0x5555_5555_5555_5555, unchecked((long)0xAAAA_AAAA_AAAA_AAAA), 0x1234_5678_9ABC_DEF0,
            ];

            private static readonly double[] s_floats =
            [
                0, -0.0, 0.5, -0.5, 1.5, -1.5, 3.7, -3.7, 255.9, 65535.5, -32768.7,
                2147483520.0, 2147483648.0, -2147483904.0, 4294967040.0, 4294967296.0, 1e10, -1e10,
                double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            ];

            private static readonly int[] s_counts = [0, 1, 7, 8, 15, 16, 31, 32, 63, 64, 67, -1];

            private static int s_check;
            private static int s_first;

            private static void Check(bool passed)
            {
                s_check++;
                if (!passed && s_first == 0) s_first = s_check;
            }

            // Lane i of a seed's vector of T, kept away from the vectors so the
            // expected values use no vector instruction.
            private static T Pattern<T>(int seed, int lane) where T : IBinaryInteger<T> =>
                T.CreateTruncating(s_patterns[(seed * 7 + lane * 11) % s_patterns.Length]);

            private static Vector128<T> V<T>(int seed) where T : unmanaged, IBinaryInteger<T>
            {
                Span<T> lanes = stackalloc T[Vector128<T>.Count];
                for (var i = 0; i < lanes.Length; i++) lanes[i] = Pattern<T>(seed, i);
                return Vector128.Create((ReadOnlySpan<T>)lanes);
            }

            private static T FloatPattern<T>(int seed, int lane) where T : IFloatingPoint<T> =>
                T.CreateTruncating(s_floats[(seed * 5 + lane * 3) % s_floats.Length]);

            private static Vector128<T> F<T>(int seed) where T : unmanaged, IFloatingPoint<T>
            {
                Span<T> lanes = stackalloc T[Vector128<T>.Count];
                for (var i = 0; i < lanes.Length; i++) lanes[i] = FloatPattern<T>(seed, i);
                return Vector128.Create((ReadOnlySpan<T>)lanes);
            }

            private static int Bits<T>() where T : unmanaged => Unsafe.SizeOf<T>() * 8;

            // The lane's bits read as a signed and as an unsigned number.
            private static long Signed<T>(T value) where T : unmanaged, IBinaryInteger<T> =>
                (long.CreateTruncating(value) << (64 - Bits<T>())) >> (64 - Bits<T>());

            private static ulong Unsigned<T>(T value) where T : unmanaged, IBinaryInteger<T> =>
                Bits<T>() == 64 ? ulong.CreateTruncating(value) : ulong.CreateTruncating(value) & ((1UL << Bits<T>()) - 1);

            // The lane as its type reads it.
            private static long Value<T>(T value) where T : unmanaged, IBinaryInteger<T> =>
                T.IsNegative(T.Zero - T.One) ? Signed(value) : unchecked((long)Unsigned(value));

            private static T ShiftedLeft<T>(T value, int count) where T : unmanaged, IBinaryInteger<T> =>
                T.CreateTruncating(Unsigned(value) << count);

            private static T ShiftedArithmetic<T>(T value, int count) where T : unmanaged, IBinaryInteger<T> =>
                T.CreateTruncating(Signed(value) >> count);

            private static T ShiftedLogical<T>(T value, int count) where T : unmanaged, IBinaryInteger<T> =>
                T.CreateTruncating(Unsigned(value) >> count);

            private static void Compare<T>(Vector128<T> actual, int x, int y, Func<T, T, bool> holds)
                where T : unmanaged, IBinaryInteger<T>
            {
                var passed = true;
                for (var i = 0; i < Vector128<T>.Count; i++)
                    passed &= actual[i] == (holds(Pattern<T>(x, i), Pattern<T>(y, i)) ? T.AllBitsSet : T.Zero);
                Check(passed);
            }

            private static void Lanewise<T>(Vector128<T> actual, int x, int y, Func<T, T, T> operation)
                where T : unmanaged, IBinaryInteger<T>
            {
                var passed = true;
                for (var i = 0; i < Vector128<T>.Count; i++)
                    passed &= actual[i] == operation(Pattern<T>(x, i), Pattern<T>(y, i));
                Check(passed);
            }

            // A shift takes its count modulo the lane width.
            private static void Shifted<T>(Func<Vector128<T>, int, Vector128<T>> shift, int x, Func<T, int, T> operation)
                where T : unmanaged, IBinaryInteger<T>
            {
                var passed = true;
                foreach (var count in s_counts)
                {
                    var actual = shift(V<T>(x), count);
                    for (var i = 0; i < Vector128<T>.Count; i++)
                        passed &= actual[i] == operation(Pattern<T>(x, i), count & (Bits<T>() - 1));
                }
                Check(passed);
            }

            private static void Widened<TFrom, TTo>(Vector128<TTo> actual, int x, int offset, bool signExtend)
                where TFrom : unmanaged, IBinaryInteger<TFrom> where TTo : unmanaged, IBinaryInteger<TTo>
            {
                var passed = true;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                {
                    var lane = Pattern<TFrom>(x, i + offset);
                    passed &= actual[i] == TTo.CreateTruncating(signExtend ? Signed(lane) : unchecked((long)Unsigned(lane)));
                }
                Check(passed);
            }

            private static void WideMultiplied<TFrom, TTo>(Vector128<TTo> actual, int x, int y, int offset)
                where TFrom : unmanaged, IBinaryInteger<TFrom> where TTo : unmanaged, IBinaryInteger<TTo>
            {
                var passed = true;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                    passed &= actual[i] == TTo.CreateTruncating(unchecked(
                        Value(Pattern<TFrom>(x, i + offset)) * Value(Pattern<TFrom>(y, i + offset))));
                Check(passed);
            }

            private static void PairwiseWidened<TFrom, TTo>(Vector128<TTo> actual, int x)
                where TFrom : unmanaged, IBinaryInteger<TFrom> where TTo : unmanaged, IBinaryInteger<TTo>
            {
                var passed = true;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                    passed &= actual[i] == TTo.CreateTruncating(
                        Value(Pattern<TFrom>(x, 2 * i)) + Value(Pattern<TFrom>(x, 2 * i + 1)));
                Check(passed);
            }

            // The first operand's lanes, then the second's, each saturated.
            private static void Narrowed<TFrom, TTo>(Vector128<TTo> actual, int x, int y)
                where TFrom : unmanaged, IBinaryInteger<TFrom> where TTo : unmanaged, IBinaryInteger<TTo>
            {
                var passed = true;
                var half = Vector128<TFrom>.Count;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                    passed &= actual[i] == TTo.CreateSaturating(
                        Value(i < half ? Pattern<TFrom>(x, i) : Pattern<TFrom>(y, i - half)));
                Check(passed);
            }

            private static void Extracted<T, TResult>(TResult actual, int x, int lane)
                where T : unmanaged, IBinaryInteger<T> where TResult : unmanaged, IBinaryInteger<TResult> =>
                Check(actual == TResult.CreateTruncating(Value(Pattern<T>(x, lane))));

            private static void Converted<TFrom, TTo>(Vector128<TTo> actual, int x)
                where TFrom : unmanaged, IBinaryInteger<TFrom> where TTo : unmanaged, IFloatingPoint<TTo>
            {
                var passed = true;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                    passed &= actual[i] == TTo.CreateTruncating(Value(Pattern<TFrom>(x, i)));
                Check(passed);
            }

            // Truncation saturates and reads NaN as zero; two doubles fill the
            // first two lanes and zero the rest.
            private static void Truncated<TFrom, TTo>(Vector128<TTo> actual, int x)
                where TFrom : unmanaged, IFloatingPoint<TFrom> where TTo : unmanaged, IBinaryInteger<TTo>
            {
                var passed = true;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                {
                    var value = FloatPattern<TFrom>(x, i);
                    passed &= actual[i] == (i >= Vector128<TFrom>.Count || TFrom.IsNaN(value) ? TTo.Zero : TTo.CreateSaturating(value));
                }
                Check(passed);
            }

            private static void LoadedWidened<TFrom, TTo>(Func<IntPtr, Vector128<TTo>> load, int x)
                where TFrom : unmanaged, IBinaryInteger<TFrom> where TTo : unmanaged, IBinaryInteger<TTo>
            {
                var memory = stackalloc TFrom[Vector128<TTo>.Count];
                for (var i = 0; i < Vector128<TTo>.Count; i++) memory[i] = Pattern<TFrom>(x, i);
                var actual = load((IntPtr)memory);
                var passed = true;
                for (var i = 0; i < Vector128<TTo>.Count; i++)
                    passed &= actual[i] == TTo.CreateTruncating(Value(Pattern<TFrom>(x, i)));
                Check(passed);
            }

            // 0 when every check passes, or the first failing check's number:
            // the check times the seed count plus one.
            public static int Run(int input)
            {
                for (var x = 0; x < s_patterns.Length; x++)
                {
                    var y = (x + 3 + input) % s_patterns.Length;
                    Extracted<sbyte, int>(PackedSimd.ExtractScalar(V<sbyte>(x), 0), x, 0);
                    Extracted<sbyte, int>(PackedSimd.ExtractScalar(V<sbyte>(x), 15), x, 15);
                    Extracted<byte, uint>(PackedSimd.ExtractScalar(V<byte>(x), 0), x, 0);
                    Extracted<byte, uint>(PackedSimd.ExtractScalar(V<byte>(x), 15), x, 15);
                    Extracted<short, int>(PackedSimd.ExtractScalar(V<short>(x), 0), x, 0);
                    Extracted<short, int>(PackedSimd.ExtractScalar(V<short>(x), 7), x, 7);
                    Extracted<ushort, uint>(PackedSimd.ExtractScalar(V<ushort>(x), 0), x, 0);
                    Extracted<ushort, uint>(PackedSimd.ExtractScalar(V<ushort>(x), 7), x, 7);
                    Extracted<int, int>(PackedSimd.ExtractScalar(V<int>(x), 0), x, 0);
                    Extracted<int, int>(PackedSimd.ExtractScalar(V<int>(x), 3), x, 3);
                    Extracted<uint, uint>(PackedSimd.ExtractScalar(V<uint>(x), 0), x, 0);
                    Extracted<uint, uint>(PackedSimd.ExtractScalar(V<uint>(x), 3), x, 3);
                    Extracted<long, long>(PackedSimd.ExtractScalar(V<long>(x), 0), x, 0);
                    Extracted<long, long>(PackedSimd.ExtractScalar(V<long>(x), 1), x, 1);
                    Extracted<ulong, ulong>(PackedSimd.ExtractScalar(V<ulong>(x), 0), x, 0);
                    Extracted<ulong, ulong>(PackedSimd.ExtractScalar(V<ulong>(x), 1), x, 1);
                    Extracted<nint, nint>(PackedSimd.ExtractScalar(V<nint>(x), 0), x, 0);
                    Extracted<nint, nint>(PackedSimd.ExtractScalar(V<nint>(x), 1), x, 1);
                    Extracted<nuint, nuint>(PackedSimd.ExtractScalar(V<nuint>(x), 0), x, 0);
                    Extracted<nuint, nuint>(PackedSimd.ExtractScalar(V<nuint>(x), 1), x, 1);
                    WideMultiplied<sbyte, short>(PackedSimd.MultiplyWideningLower(V<sbyte>(x), V<sbyte>(y)), x, y, 0);
                    WideMultiplied<byte, ushort>(PackedSimd.MultiplyWideningLower(V<byte>(x), V<byte>(y)), x, y, 0);
                    WideMultiplied<short, int>(PackedSimd.MultiplyWideningLower(V<short>(x), V<short>(y)), x, y, 0);
                    WideMultiplied<ushort, uint>(PackedSimd.MultiplyWideningLower(V<ushort>(x), V<ushort>(y)), x, y, 0);
                    WideMultiplied<int, long>(PackedSimd.MultiplyWideningLower(V<int>(x), V<int>(y)), x, y, 0);
                    WideMultiplied<uint, ulong>(PackedSimd.MultiplyWideningLower(V<uint>(x), V<uint>(y)), x, y, 0);
                    WideMultiplied<sbyte, short>(PackedSimd.MultiplyWideningUpper(V<sbyte>(x), V<sbyte>(y)), x, y, Vector128<short>.Count);
                    WideMultiplied<byte, ushort>(PackedSimd.MultiplyWideningUpper(V<byte>(x), V<byte>(y)), x, y, Vector128<ushort>.Count);
                    WideMultiplied<short, int>(PackedSimd.MultiplyWideningUpper(V<short>(x), V<short>(y)), x, y, Vector128<int>.Count);
                    WideMultiplied<ushort, uint>(PackedSimd.MultiplyWideningUpper(V<ushort>(x), V<ushort>(y)), x, y, Vector128<uint>.Count);
                    WideMultiplied<int, long>(PackedSimd.MultiplyWideningUpper(V<int>(x), V<int>(y)), x, y, Vector128<long>.Count);
                    WideMultiplied<uint, ulong>(PackedSimd.MultiplyWideningUpper(V<uint>(x), V<uint>(y)), x, y, Vector128<ulong>.Count);
                    PairwiseWidened<sbyte, short>(PackedSimd.AddPairwiseWidening(V<sbyte>(x)), x);
                    PairwiseWidened<byte, ushort>(PackedSimd.AddPairwiseWidening(V<byte>(x)), x);
                    PairwiseWidened<short, int>(PackedSimd.AddPairwiseWidening(V<short>(x)), x);
                    PairwiseWidened<ushort, uint>(PackedSimd.AddPairwiseWidening(V<ushort>(x)), x);
                    Lanewise(PackedSimd.AddSaturate(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => sbyte.CreateSaturating((long)l + r));
                    Lanewise(PackedSimd.AddSaturate(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => byte.CreateSaturating((long)l + r));
                    Lanewise(PackedSimd.AddSaturate(V<short>(x), V<short>(y)), x, y, static (short l, short r) => short.CreateSaturating((long)l + r));
                    Lanewise(PackedSimd.AddSaturate(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => ushort.CreateSaturating((long)l + r));
                    Lanewise(PackedSimd.SubtractSaturate(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => sbyte.CreateSaturating((long)l - r));
                    Lanewise(PackedSimd.SubtractSaturate(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => byte.CreateSaturating((long)l - r));
                    Lanewise(PackedSimd.SubtractSaturate(V<short>(x), V<short>(y)), x, y, static (short l, short r) => short.CreateSaturating((long)l - r));
                    Lanewise(PackedSimd.SubtractSaturate(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => ushort.CreateSaturating((long)l - r));
                    Lanewise(PackedSimd.Min(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => l < r ? l : r);
                    Lanewise(PackedSimd.Min(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => l < r ? l : r);
                    Lanewise(PackedSimd.Min(V<short>(x), V<short>(y)), x, y, static (short l, short r) => l < r ? l : r);
                    Lanewise(PackedSimd.Min(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => l < r ? l : r);
                    Lanewise(PackedSimd.Min(V<int>(x), V<int>(y)), x, y, static (int l, int r) => l < r ? l : r);
                    Lanewise(PackedSimd.Min(V<uint>(x), V<uint>(y)), x, y, static (uint l, uint r) => l < r ? l : r);
                    Lanewise(PackedSimd.Max(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => l > r ? l : r);
                    Lanewise(PackedSimd.Max(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => l > r ? l : r);
                    Lanewise(PackedSimd.Max(V<short>(x), V<short>(y)), x, y, static (short l, short r) => l > r ? l : r);
                    Lanewise(PackedSimd.Max(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => l > r ? l : r);
                    Lanewise(PackedSimd.Max(V<int>(x), V<int>(y)), x, y, static (int l, int r) => l > r ? l : r);
                    Lanewise(PackedSimd.Max(V<uint>(x), V<uint>(y)), x, y, static (uint l, uint r) => l > r ? l : r);
                    Lanewise(PackedSimd.AverageRounded(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => (byte)(((uint)l + r + 1) >> 1));
                    Lanewise(PackedSimd.AverageRounded(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => (ushort)(((uint)l + r + 1) >> 1));
                    Shifted<sbyte>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<sbyte>);
                    Shifted<byte>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<byte>);
                    Shifted<short>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<short>);
                    Shifted<ushort>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<ushort>);
                    Shifted<int>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<int>);
                    Shifted<uint>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<uint>);
                    Shifted<long>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<long>);
                    Shifted<ulong>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<ulong>);
                    Shifted<nint>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<nint>);
                    Shifted<nuint>(static (v, c) => PackedSimd.ShiftLeft(v, c), x, ShiftedLeft<nuint>);
                    Shifted<sbyte>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<sbyte>);
                    Shifted<byte>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<byte>);
                    Shifted<short>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<short>);
                    Shifted<ushort>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<ushort>);
                    Shifted<int>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<int>);
                    Shifted<uint>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<uint>);
                    Shifted<long>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<long>);
                    Shifted<ulong>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<ulong>);
                    Shifted<nint>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<nint>);
                    Shifted<nuint>(static (v, c) => PackedSimd.ShiftRightArithmetic(v, c), x, ShiftedArithmetic<nuint>);
                    Shifted<sbyte>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<sbyte>);
                    Shifted<byte>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<byte>);
                    Shifted<short>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<short>);
                    Shifted<ushort>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<ushort>);
                    Shifted<int>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<int>);
                    Shifted<uint>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<uint>);
                    Shifted<long>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<long>);
                    Shifted<ulong>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<ulong>);
                    Shifted<nint>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<nint>);
                    Shifted<nuint>(static (v, c) => PackedSimd.ShiftRightLogical(v, c), x, ShiftedLogical<nuint>);
                    Compare(PackedSimd.CompareLessThan(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<short>(x), V<short>(y)), x, y, static (short l, short r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<int>(x), V<int>(y)), x, y, static (int l, int r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<uint>(x), V<uint>(y)), x, y, static (uint l, uint r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<long>(x), V<long>(y)), x, y, static (long l, long r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<ulong>(x), V<ulong>(y)), x, y, static (ulong l, ulong r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<nint>(x), V<nint>(y)), x, y, static (nint l, nint r) => l < r);
                    Compare(PackedSimd.CompareLessThan(V<nuint>(x), V<nuint>(y)), x, y, static (nuint l, nuint r) => l < r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<short>(x), V<short>(y)), x, y, static (short l, short r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<int>(x), V<int>(y)), x, y, static (int l, int r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<uint>(x), V<uint>(y)), x, y, static (uint l, uint r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<long>(x), V<long>(y)), x, y, static (long l, long r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<ulong>(x), V<ulong>(y)), x, y, static (ulong l, ulong r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<nint>(x), V<nint>(y)), x, y, static (nint l, nint r) => l <= r);
                    Compare(PackedSimd.CompareLessThanOrEqual(V<nuint>(x), V<nuint>(y)), x, y, static (nuint l, nuint r) => l <= r);
                    Compare(PackedSimd.CompareGreaterThan(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<short>(x), V<short>(y)), x, y, static (short l, short r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<int>(x), V<int>(y)), x, y, static (int l, int r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<uint>(x), V<uint>(y)), x, y, static (uint l, uint r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<long>(x), V<long>(y)), x, y, static (long l, long r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<ulong>(x), V<ulong>(y)), x, y, static (ulong l, ulong r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<nint>(x), V<nint>(y)), x, y, static (nint l, nint r) => l > r);
                    Compare(PackedSimd.CompareGreaterThan(V<nuint>(x), V<nuint>(y)), x, y, static (nuint l, nuint r) => l > r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<sbyte>(x), V<sbyte>(y)), x, y, static (sbyte l, sbyte r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<byte>(x), V<byte>(y)), x, y, static (byte l, byte r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<short>(x), V<short>(y)), x, y, static (short l, short r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<ushort>(x), V<ushort>(y)), x, y, static (ushort l, ushort r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<int>(x), V<int>(y)), x, y, static (int l, int r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<uint>(x), V<uint>(y)), x, y, static (uint l, uint r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<long>(x), V<long>(y)), x, y, static (long l, long r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<ulong>(x), V<ulong>(y)), x, y, static (ulong l, ulong r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<nint>(x), V<nint>(y)), x, y, static (nint l, nint r) => l >= r);
                    Compare(PackedSimd.CompareGreaterThanOrEqual(V<nuint>(x), V<nuint>(y)), x, y, static (nuint l, nuint r) => l >= r);
                    LoadedWidened<sbyte, short>(static p => PackedSimd.LoadWideningVector128((sbyte*)p), x);
                    LoadedWidened<byte, ushort>(static p => PackedSimd.LoadWideningVector128((byte*)p), x);
                    LoadedWidened<short, int>(static p => PackedSimd.LoadWideningVector128((short*)p), x);
                    LoadedWidened<ushort, uint>(static p => PackedSimd.LoadWideningVector128((ushort*)p), x);
                    LoadedWidened<int, long>(static p => PackedSimd.LoadWideningVector128((int*)p), x);
                    LoadedWidened<uint, ulong>(static p => PackedSimd.LoadWideningVector128((uint*)p), x);
                    Converted<int, float>(PackedSimd.ConvertToSingle(V<int>(x)), x);
                    Converted<uint, float>(PackedSimd.ConvertToSingle(V<uint>(x)), x);
                    Converted<int, double>(PackedSimd.ConvertToDoubleLower(V<int>(x)), x);
                    Converted<uint, double>(PackedSimd.ConvertToDoubleLower(V<uint>(x)), x);
                    Truncated<float, int>(PackedSimd.ConvertToInt32Saturate(F<float>(x)), x);
                    Truncated<float, uint>(PackedSimd.ConvertToUInt32Saturate(F<float>(x)), x);
                    Truncated<double, int>(PackedSimd.ConvertToInt32Saturate(F<double>(x)), x);
                    Truncated<double, uint>(PackedSimd.ConvertToUInt32Saturate(F<double>(x)), x);
                    Narrowed<short, sbyte>(PackedSimd.ConvertNarrowingSaturateSigned(V<short>(x), V<short>(y)), x, y);
                    Narrowed<int, short>(PackedSimd.ConvertNarrowingSaturateSigned(V<int>(x), V<int>(y)), x, y);
                    Narrowed<short, byte>(PackedSimd.ConvertNarrowingSaturateUnsigned(V<short>(x), V<short>(y)), x, y);
                    Narrowed<int, ushort>(PackedSimd.ConvertNarrowingSaturateUnsigned(V<int>(x), V<int>(y)), x, y);
                    Widened<sbyte, short>(PackedSimd.SignExtendWideningLower(V<sbyte>(x)), x, 0, true);
                    Widened<byte, ushort>(PackedSimd.SignExtendWideningLower(V<byte>(x)), x, 0, true);
                    Widened<short, int>(PackedSimd.SignExtendWideningLower(V<short>(x)), x, 0, true);
                    Widened<ushort, uint>(PackedSimd.SignExtendWideningLower(V<ushort>(x)), x, 0, true);
                    Widened<int, long>(PackedSimd.SignExtendWideningLower(V<int>(x)), x, 0, true);
                    Widened<uint, ulong>(PackedSimd.SignExtendWideningLower(V<uint>(x)), x, 0, true);
                    Widened<sbyte, short>(PackedSimd.SignExtendWideningUpper(V<sbyte>(x)), x, Vector128<short>.Count, true);
                    Widened<byte, ushort>(PackedSimd.SignExtendWideningUpper(V<byte>(x)), x, Vector128<ushort>.Count, true);
                    Widened<short, int>(PackedSimd.SignExtendWideningUpper(V<short>(x)), x, Vector128<int>.Count, true);
                    Widened<ushort, uint>(PackedSimd.SignExtendWideningUpper(V<ushort>(x)), x, Vector128<uint>.Count, true);
                    Widened<int, long>(PackedSimd.SignExtendWideningUpper(V<int>(x)), x, Vector128<long>.Count, true);
                    Widened<uint, ulong>(PackedSimd.SignExtendWideningUpper(V<uint>(x)), x, Vector128<ulong>.Count, true);
                    Widened<sbyte, short>(PackedSimd.ZeroExtendWideningLower(V<sbyte>(x)), x, 0, false);
                    Widened<byte, ushort>(PackedSimd.ZeroExtendWideningLower(V<byte>(x)), x, 0, false);
                    Widened<short, int>(PackedSimd.ZeroExtendWideningLower(V<short>(x)), x, 0, false);
                    Widened<ushort, uint>(PackedSimd.ZeroExtendWideningLower(V<ushort>(x)), x, 0, false);
                    Widened<int, long>(PackedSimd.ZeroExtendWideningLower(V<int>(x)), x, 0, false);
                    Widened<uint, ulong>(PackedSimd.ZeroExtendWideningLower(V<uint>(x)), x, 0, false);
                    Widened<sbyte, short>(PackedSimd.ZeroExtendWideningUpper(V<sbyte>(x)), x, Vector128<short>.Count, false);
                    Widened<byte, ushort>(PackedSimd.ZeroExtendWideningUpper(V<byte>(x)), x, Vector128<ushort>.Count, false);
                    Widened<short, int>(PackedSimd.ZeroExtendWideningUpper(V<short>(x)), x, Vector128<int>.Count, false);
                    Widened<ushort, uint>(PackedSimd.ZeroExtendWideningUpper(V<ushort>(x)), x, Vector128<uint>.Count, false);
                    Widened<int, long>(PackedSimd.ZeroExtendWideningUpper(V<int>(x)), x, Vector128<long>.Count, false);
                    Widened<uint, ulong>(PackedSimd.ZeroExtendWideningUpper(V<uint>(x)), x, Vector128<ulong>.Count, false);
                }
                return s_first;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void LanesComputeTheirElementTypesArithmetic(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("PackedSimdLaneSemantics", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var first = ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd);

        Assert.True(first == 0, first == 0 ? null : $"{Checks[(first - 1) % Checks.Length]} fails for seed {(first - 1) / Checks.Length}");
    }
}
