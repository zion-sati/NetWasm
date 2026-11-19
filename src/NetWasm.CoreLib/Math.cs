// Adapted from dotnet/runtime System.Private.CoreLib Math.cs and MathF.cs.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
// Source reference: dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>Common mathematical constants and scalar operations.</summary>
    public static partial class Math
    {
        // The largest digit count the fast rounding path handles: `10^digits` must fit a `ulong` for the
        // integer fallback (10^19 is the last that does); larger counts use the exact routine.
        private const int maxFastRoundingDigits = 19;

        // Below this boundary a double may have a fractional portion; at or above it every
        // representable value is already an integer (2^52).
        private const double doubleIntegerBoundary = 4503599627370496.0;

        public const double E = 2.718281828459045;
        public const double PI = 3.141592653589793;
        public const double Tau = 6.283185307179586;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Abs(double value) =>
            BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(value) & 0x7FFFFFFFFFFFFFFFUL);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float value) =>
            BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(value) & 0x7FFFFFFFU);
        [Intrinsic]
        public static double Ceiling(double value) => CeilingCore(value);
        [Intrinsic]
        public static double Floor(double value) => FloorCore(value);
        [Intrinsic]
        public static double Truncate(double value) => TruncateCore(value);
        [Intrinsic]
        public static double Sqrt(double value) => SqrtCore(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Abs(short value)
        {
            if (value < 0)
            {
                value = (short)-value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(int value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Abs(long value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Abs(nint value)
        {
            if (value < 0)
            {
                value = -value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Abs(sbyte value)
        {
            if (value < 0)
            {
                value = (sbyte)-value;
                if (value < 0)
                {
                    ThrowNegateTwosCompOverflow();
                }
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Abs(decimal value) => decimal.Abs(value);

        [DoesNotReturn]
        [StackTraceHidden]
        internal static void ThrowNegateTwosCompOverflow()
        {
            throw new OverflowException(SR.Overflow_NegateTwosCompNum);
        }

        [Intrinsic]
        public static double Asinh(double value)
        {
            if (IsNaN(value) || IsInfinity(value) || value == 0) return value;
            var absolute = value < 0 ? -value : value;
            if (absolute < 1e-8) return value;
            var result = absolute > 1e154
                ? Log(absolute) + 0.6931471805599453
                : Log(absolute + Sqrt(absolute * absolute + 1));
            return value < 0 ? -result : result;
        }

        [Intrinsic]
        public static double Atanh(double value)
        {
            if (IsNaN(value)) return value;
            if (value == 1) return double.PositiveInfinity;
            if (value == -1) return double.NegativeInfinity;
            if (value > 1 || value < -1) return double.NaN;
            if (value == 0) return value;
            if (Abs(value) < 1e-8) return value;
            return 0.5 * Log((1 + value) / (1 - value));
        }

        public static long BigMul(int a, int b) => (long)a * b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong BigMul(uint a, uint b) => (ulong)a * b;

        public static long BigMul(long a, long b, out long low)
        {
            var high = BigMul((ulong)a, (ulong)b, out var unsignedLow);
            low = (long)unsignedLow;
            return (long)high - ((a >> 63) & b) - ((b >> 63) & a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong BigMul(ulong a, ulong b, out ulong low)
        {
            var al = (uint)a;
            var ah = (uint)(a >> 32);
            var bl = (uint)b;
            var bh = (uint)(b >> 32);
            var mull = (ulong)al * bl;
            var t = (ulong)ah * bl + (mull >> 32);
            var tl = (ulong)al * bh + (uint)t;
            low = (tl << 32) | (uint)mull;
            return (ulong)ah * bh + (t >> 32) + (tl >> 32);
        }

        public static double BitDecrement(double value)
        {
            if (IsNaN(value) || value == double.NegativeInfinity) return value;
            if (value == double.PositiveInfinity) return double.MaxValue;
            if (value == 0) return -double.Epsilon;
            // BitConverter is lowered by the compiler's existing bit intrinsics.
            var bits = BitConverter.DoubleToUInt64Bits(value);
            bits = IsNegative(value) ? bits + 1 : bits - 1;
            return BitConverter.UInt64BitsToDouble(bits);
        }

        public static double BitIncrement(double value)
        {
            if (IsNaN(value) || value == double.PositiveInfinity) return value;
            if (value == double.NegativeInfinity) return double.MinValue;
            if (value == 0) return double.Epsilon;
            var bits = BitConverter.DoubleToUInt64Bits(value);
            bits = IsNegative(value) ? bits - 1 : bits + 1;
            return BitConverter.UInt64BitsToDouble(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Ceiling(decimal value) => decimal.Ceiling(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(byte value, byte minimum, byte maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Clamp(short value, short minimum, short maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int minimum, int maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Clamp(long value, long minimum, long maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Clamp(nint value, nint minimum, nint maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Clamp(sbyte value, sbyte minimum, sbyte maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Clamp(ushort value, ushort minimum, ushort maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Clamp(uint value, uint minimum, uint maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Clamp(ulong value, ulong minimum, ulong maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint Clamp(nuint value, nuint minimum, nuint maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float value, float minimum, float maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double value, double minimum, double maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            if (minimum > maximum)
            {
                ThrowMinMaxException();
            }

            if (value < minimum)
            {
                return minimum;
            }
            else if (value > maximum)
            {
                return maximum;
            }

            return value;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CopySign(double value, double sign)
        {
            var magnitude = BitConverter.DoubleToUInt64Bits(value) & 0x7FFFFFFFFFFFFFFFUL;
            var signBit = BitConverter.DoubleToUInt64Bits(sign) & 0x8000000000000000UL;
            return BitConverter.UInt64BitsToDouble(magnitude | signBit);
        }

        [Intrinsic]
        public static double Cosh(double value)
        {
            if (IsNaN(value)) return value;
            if (IsInfinity(value)) return double.PositiveInfinity;
            var absolute = value < 0 ? -value : value;

            // For large finite values, form exp(x) / 2 without first
            // overflowing exp(x). This is the scalar libm shape used near the
            // upper finite boundary.
            if (absolute >= 22)
            {
                if (absolute <= 709.782712893384)
                {
                    return 0.5 * Exp(absolute);
                }

                var halfExponential = Exp(0.5 * absolute);
                return (0.5 * halfExponential) * halfExponential;
            }

            var exponential = Exp(absolute);
            return 0.5 * (exponential + 1 / exponential);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (sbyte Quotient, sbyte Remainder) DivRem(sbyte left, sbyte right)
        {
            var quotient = (sbyte)(left / right);
            return (quotient, (sbyte)(left - quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (byte Quotient, byte Remainder) DivRem(byte left, byte right)
        {
            var quotient = (byte)(left / right);
            return (quotient, (byte)(left - quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (short Quotient, short Remainder) DivRem(short left, short right)
        {
            var quotient = (short)(left / right);
            return (quotient, (short)(left - quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ushort Quotient, ushort Remainder) DivRem(ushort left, ushort right)
        {
            var quotient = (ushort)(left / right);
            return (quotient, (ushort)(left - quotient * right));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int Quotient, int Remainder) DivRem(int left, int right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        public static int DivRem(int left, int right, out int remainder)
        {
            var quotient = left / right;
            remainder = left - quotient * right;
            return quotient;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (uint Quotient, uint Remainder) DivRem(uint left, uint right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (long Quotient, long Remainder) DivRem(long left, long right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        public static long DivRem(long left, long right, out long remainder)
        {
            var quotient = left / right;
            remainder = left - quotient * right;
            return quotient;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (ulong Quotient, ulong Remainder) DivRem(ulong left, ulong right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (nint Quotient, nint Remainder) DivRem(nint left, nint right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (nuint Quotient, nuint Remainder) DivRem(nuint left, nuint right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Floor(decimal value) => decimal.Floor(value);

        public static double IEEERemainder(double x, double y)
        {
            if (IsNaN(x)) return x;
            if (IsNaN(y)) return y;
            var remainder = x % y;
            if (IsNaN(remainder)) return double.NaN;
            if (remainder == 0 && IsNegative(x)) return double.NegativeZero;
            var alternative = remainder - Abs(y) * Sign(x);
            if (Abs(alternative) == Abs(remainder))
            {
                var quotient = x / y;
                var rounded = RoundCore(quotient);
                return Abs(rounded) > Abs(quotient) ? alternative : remainder;
            }
            return Abs(alternative) < Abs(remainder) ? alternative : remainder;
        }

        public static int ILogB(double value)
        {
            if (value == 0) return int.MinValue;
            if (IsNaN(value) || IsInfinity(value)) return int.MaxValue;
            var absolute = value < 0 ? -value : value;
            var exponent = 0;
            if (absolute >= 1)
            {
                while (absolute >= 2) { absolute /= 2; exponent++; }
            }
            else
            {
                while (absolute < 1) { absolute *= 2; exponent--; }
            }
            return exponent;
        }

        public static double Log(double value, double newBase)
        {
            if (IsNaN(value)) return value;
            if (IsNaN(newBase)) return newBase;
            if (newBase == 1 || (value != 1 && (newBase == 0 || newBase == double.PositiveInfinity))) return double.NaN;
            return Log(value) / Log(newBase);
        }

        [Intrinsic]
        public static double Log10(double value) => Log(value) / 2.302585092994046;
        [Intrinsic]
        public static double Log2(double value) => Log(value) / 0.6931471805599453;

        [Intrinsic]
        public static byte Max(byte left, byte right) => left >= right ? left : right;
        [Intrinsic]
        public static short Max(short left, short right) => left >= right ? left : right;
        [Intrinsic]
        public static int Max(int left, int right) => left >= right ? left : right;
        [Intrinsic]
        public static long Max(long left, long right) => left >= right ? left : right;
        [Intrinsic]
        public static nint Max(nint left, nint right) => left >= right ? left : right;
        [Intrinsic]
        public static sbyte Max(sbyte left, sbyte right) => left >= right ? left : right;
        [Intrinsic]
        public static ushort Max(ushort left, ushort right) => left >= right ? left : right;
        [Intrinsic]
        public static uint Max(uint left, uint right) => left >= right ? left : right;
        [Intrinsic]
        public static ulong Max(ulong left, ulong right) => left >= right ? left : right;
        [Intrinsic]
        public static nuint Max(nuint left, nuint right) => left >= right ? left : right;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Max(decimal left, decimal right) => decimal.Max(left, right);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Max(double left, double right)
        {
            if (left != right) return !IsNaN(left) ? left > right ? left : right : left;
            return IsNegative(right) ? left : right;
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float left, float right)
        {
            if (left != right) return !IsNaN(left) ? left > right ? left : right : left;
            return IsNegative(right) ? left : right;
        }

        [Intrinsic]
        public static double MaxMagnitude(double x, double y)
        {
            // This matches the IEEE 754:2019 `maximumMagnitude` function
            //
            // It propagates NaN inputs back to the caller and
            // otherwise returns the input with a greater magnitude.
            // It treats +0 as greater than -0 as per the specification.

            double ax = Abs(x);
            double ay = Abs(y);

            if ((ax > ay) || double.IsNaN(ax))
            {
                return x;
            }

            if (ax == ay)
            {
                return double.IsNegative(x) ? y : x;
            }

            return y;
        }

        [Intrinsic]
        public static byte Min(byte left, byte right) => left <= right ? left : right;
        [Intrinsic]
        public static short Min(short left, short right) => left <= right ? left : right;
        [Intrinsic]
        public static int Min(int left, int right) => left <= right ? left : right;
        [Intrinsic]
        public static long Min(long left, long right) => left <= right ? left : right;
        [Intrinsic]
        public static nint Min(nint left, nint right) => left <= right ? left : right;
        [Intrinsic]
        public static sbyte Min(sbyte left, sbyte right) => left <= right ? left : right;
        [Intrinsic]
        public static ushort Min(ushort left, ushort right) => left <= right ? left : right;
        [Intrinsic]
        public static uint Min(uint left, uint right) => left <= right ? left : right;
        [Intrinsic]
        public static ulong Min(ulong left, ulong right) => left <= right ? left : right;
        [Intrinsic]
        public static nuint Min(nuint left, nuint right) => left <= right ? left : right;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Min(decimal left, decimal right) => decimal.Min(left, right);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Min(double left, double right)
        {
            if (left != right) return !IsNaN(left) ? left < right ? left : right : left;
            return IsNegative(left) ? left : right;
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float left, float right)
        {
            if (left != right) return !IsNaN(left) ? left < right ? left : right : left;
            return IsNegative(left) ? left : right;
        }

        [Intrinsic]
        public static double MinMagnitude(double x, double y)
        {
            // This matches the IEEE 754:2019 `minimumMagnitude` function
            //
            // It propagates NaN inputs back to the caller and
            // otherwise returns the input with a lesser magnitude.
            // It treats +0 as greater than -0 as per the specification.

            double ax = Abs(x);
            double ay = Abs(y);

            if ((ax < ay) || double.IsNaN(ax))
            {
                return x;
            }

            if (ax == ay)
            {
                return double.IsNegative(x) ? x : y;
            }

            return y;
        }

        [Intrinsic]
        public static double ReciprocalEstimate(double value) => 1 / value;
        [Intrinsic]
        public static double ReciprocalSqrtEstimate(double value) => 1 / Sqrt(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Round(decimal value) => decimal.Round(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Round(decimal value, int decimals) => decimal.Round(value, decimals);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Round(decimal value, MidpointRounding mode) => decimal.Round(value, mode);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Round(decimal value, int decimals, MidpointRounding mode) => decimal.Round(value, decimals, mode);
        [Intrinsic]
        public static double Round(double value) => RoundCore(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Round(double value, int digits) => Round(value, digits, MidpointRounding.ToEven);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Round(double value, MidpointRounding mode) => RoundMode(value, mode);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Round(double value, int digits, MidpointRounding mode)
        {
            if (digits < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRange_RoundingDigits(nameof(digits));
            }

            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity)
            {
                ThrowHelper.ThrowArgumentException_InvalidEnumValue(mode);
            }

            // Rounding to zero fractional digits is just rounding to an integer, which the dedicated
            // overload does with a single hardware instruction on most platforms.
            if (digits == 0)
            {
                return Round(value, mode);
            }

            // Only finite values with a magnitude below the integer boundary can have a fractional
            // portion to round. All other values (including NaN and Infinity) are returned unchanged;
            // this comparison is naturally false for those cases.
            if (Abs(value) < doubleIntegerBoundary)
            {
                // The fast path only handles the small-digit range where `10^digits` is exactly
                // representable; larger counts fall back to the exact arbitrary-precision routine.
                if ((digits > maxFastRoundingDigits) || !Number.TryRoundToDecimalDigitsFast(value, digits, mode, out double rounded))
                {
                    rounded = Number.RoundToDecimalDigits<double>(value, digits, mode);
                }
                value = rounded;
            }

            return value;
        }

        // Upstream's managed scalbln: Math's SCALEB_C1, SCALEB_C2 and SCALEB_C3 as literals.
        public static double ScaleB(double x, int n)
        {
            // Implementation based on https://git.musl-libc.org/cgit/musl/tree/src/math/scalbln.c
            //
            // Performs the calculation x * 2^n efficiently. It constructs a double from 2^n by building
            // the correct biased exponent. If n is greater than the maximum exponent (1023) or less than
            // the minimum exponent (-1022), adjust x and n to compute correct result.

            double y = x;
            if (n > 1023)
            {
                y *= 8.98846567431158E+307; // 0x1p1023
                n -= 1023;
                if (n > 1023)
                {
                    y *= 8.98846567431158E+307;
                    n -= 1023;
                    if (n > 1023)
                    {
                        n = 1023;
                    }
                }
            }
            else if (n < -1022)
            {
                y *= 2.2250738585072014E-308 * 9007199254740992; // 0x1p-1022 * 0x1p53
                n += 1022 - 53;
                if (n < -1022)
                {
                    y *= 2.2250738585072014E-308 * 9007199254740992;
                    n += 1022 - 53;
                    if (n < -1022)
                    {
                        n = -1022;
                    }
                }
            }

            double u = BitConverter.Int64BitsToDouble(((long)(0x3ff + n) << 52));
            return y * u;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(short value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(long value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(nint value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(sbyte value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(decimal value) => decimal.Sign(value);
        public static int Sign(float value)
        {
            if (IsNaN(value)) throw new ArithmeticException();
            return value < 0 ? -1 : value > 0 ? 1 : 0;
        }

        public static int Sign(double value)
        {
            if (IsNaN(value)) throw new ArithmeticException();
            return value < 0 ? -1 : value > 0 ? 1 : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal Truncate(decimal value) => decimal.Truncate(value);

        // The throw stays out of line, so each clamp inlines into its caller.
        [DoesNotReturn]
        private static void ThrowMinMaxException() => throw new ArgumentException();

        private static double TruncateCore(double value)
        {
            if (IsNaN(value) || IsInfinity(value) || value == 0) return value;
            var result = value - value % 1;
            return result == 0 ? CopySign(0, value) : result;
        }

        private static double RoundCore(double value)
        {
            if (IsNaN(value) || IsInfinity(value) || value == 0) return value;
            if (Abs(value) >= 4503599627370496) return value;
            var truncated = TruncateCore(value);
            var difference = value - truncated;
            if (difference > 0.5 || difference == 0.5 && ((long)truncated & 1) != 0) return truncated + 1;
            if (difference < -0.5 || difference == -0.5 && ((long)truncated & 1) != 0) return truncated - 1;
            return truncated == 0 ? CopySign(0, value) : truncated;
        }

        private static double RoundMode(double value, MidpointRounding mode)
        {
            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity) throw new ArgumentException();
            if (value == 0 || IsNaN(value) || IsInfinity(value)) return value;
            return mode switch
            {
                MidpointRounding.ToEven => RoundCore(value),
                MidpointRounding.AwayFromZero => TruncateCore(value + CopySign(0.49999999999999994, value)),
                MidpointRounding.ToZero => TruncateCore(value),
                MidpointRounding.ToNegativeInfinity => FloorCore(value),
                MidpointRounding.ToPositiveInfinity => CeilingCore(value),
                _ => throw new ArgumentException(),
            };
        }

        private static double FloorCore(double value)
        {
            if (IsNaN(value) || IsInfinity(value) || value == 0) return value;
            var truncated = TruncateCore(value);
            var result = value < truncated ? truncated - 1 : truncated;
            return result == 0 ? CopySign(0, value) : result;
        }

        private static double CeilingCore(double value)
        {
            if (IsNaN(value) || IsInfinity(value) || value == 0) return value;
            var truncated = TruncateCore(value);
            var result = value > truncated ? truncated + 1 : truncated;
            return result == 0 ? CopySign(0, value) : result;
        }

        private static bool IsNaN(double value) => double.IsNaN(value);
        private static bool IsInfinity(double value) => double.IsInfinity(value);
        private static bool IsNegative(double value) => value < 0 || value == 0 && 1 / value == double.NegativeInfinity;

        private static double SqrtCore(double value)
        {
            if (IsNaN(value)) return value;
            if (value < 0) return double.NaN;
            if (value == 0 || IsInfinity(value)) return value;
            var scaled = value;
            var exponent = 0;
            while (scaled > 4) { scaled *= 0.25; exponent++; }
            while (scaled < 1) { scaled *= 4; exponent--; }
            var result = (1 + scaled) * 0.5;
            for (var iteration = 0; iteration < 8; iteration++)
            {
                result = (result + scaled / result) * 0.5;
            }
            return ScaleB(result, exponent);
        }
    }

    /// <summary>Single-precision counterparts of <see cref="Math"/> operations.</summary>
    public static partial class MathF
    {
        // The largest digit count the fast rounding path handles: `10^digits` must be exactly representable
        // as a `float` (10^10 is the last that is); larger counts use the exact routine.
        private const int maxFastRoundingDigits = 10;

        // Below this boundary a float may have a fractional portion; at or above it every
        // representable value is already an integer (2^23).
        private const float singleIntegerBoundary = 8388608.0f;

        public const float E = 2.7182817f;
        public const float PI = 3.1415927f;
        public const float Tau = 6.2831855f;

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float value) => Math.Abs(value);
        [Intrinsic]
        public static float Ceiling(float value) => (float)Math.Ceiling(value);
        [Intrinsic]
        public static float Floor(float value) => (float)Math.Floor(value);
        [Intrinsic]
        public static float Truncate(float value) => (float)Math.Truncate(value);
        [Intrinsic]
        public static float Round(float value) => (float)Math.Round(value);
        [Intrinsic]
        public static float Sqrt(float value) => (float)Math.Sqrt(value);

        [Intrinsic]
        public static float Acos(float value) => (float)Math.Acos(value);
        [Intrinsic]
        public static float Asin(float value) => (float)Math.Asin(value);
        [Intrinsic]
        public static float Asinh(float value) => (float)Math.Asinh(value);
        [Intrinsic]
        public static float Atan(float value) => (float)Math.Atan(value);
        [Intrinsic]
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        [Intrinsic]
        public static float Atanh(float value) => (float)Math.Atanh(value);
        public static float BitDecrement(float value)
        {
            var bits = BitConverter.SingleToUInt32Bits(value);
            if (!float.IsFinite(value)) return bits == 0x7F800000U ? float.MaxValue : value;
            if (bits == 0) return -float.Epsilon;
            bits = (bits & 0x80000000U) != 0 ? bits + 1 : bits - 1;
            return BitConverter.UInt32BitsToSingle(bits);
        }

        public static float BitIncrement(float value)
        {
            var bits = BitConverter.SingleToUInt32Bits(value);
            if (!float.IsFinite(value)) return bits == 0xFF800000U ? float.MinValue : value;
            if (bits == 0x80000000U) return float.Epsilon;
            bits = (bits & 0x80000000U) != 0 ? bits - 1 : bits + 1;
            return BitConverter.UInt32BitsToSingle(bits);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CopySign(float value, float sign) =>
            BitConverter.UInt32BitsToSingle(
                (BitConverter.SingleToUInt32Bits(value) & 0x7FFFFFFFU) |
                (BitConverter.SingleToUInt32Bits(sign) & 0x80000000U));
        [Intrinsic]
        public static float Cosh(float value) => (float)Math.Cosh(value);
        // A float product is exact in double, and rounding the double sum to odd
        // keeps enough bits that rounding it to float rounds the exact result once.
        public static float IEEERemainder(float x, float y)
        {
            if (float.IsNaN(x)) return x;
            if (float.IsNaN(y)) return y;
            var regular = x % y;
            if (float.IsNaN(regular)) return float.NaN;
            if (regular == 0 && IsNegative(x)) return -0.0f;
            var alternative = regular - Abs(y) * Sign(x);
            if (Abs(alternative) == Abs(regular))
            {
                var quotient = x / y;
                var rounded = Round(quotient);
                return Abs(rounded) > Abs(quotient) ? alternative : regular;
            }
            return Abs(alternative) < Abs(regular) ? alternative : regular;
        }
        public static int ILogB(float value) => Math.ILogB(value);
        public static float Log(float value, float newBase) => (float)Math.Log(value, newBase);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float left, float right)
        {
            if (left != right) return !float.IsNaN(left) ? left < right ? left : right : left;
            return IsNegative(left) ? left : right;
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float left, float right)
        {
            if (left != right) return !float.IsNaN(left) ? left > right ? left : right : left;
            return IsNegative(right) ? left : right;
        }

        [Intrinsic]
        public static float MaxMagnitude(float x, float y)
        {
            // This matches the IEEE 754:2019 `maximumMagnitude` function
            //
            // It propagates NaN inputs back to the caller and
            // otherwise returns the input with a greater magnitude.
            // It treats +0 as greater than -0 as per the specification.

            float ax = Abs(x);
            float ay = Abs(y);

            if ((ax > ay) || float.IsNaN(ax))
            {
                return x;
            }

            if (ax == ay)
            {
                return float.IsNegative(x) ? y : x;
            }

            return y;
        }

        [Intrinsic]
        public static float MinMagnitude(float x, float y)
        {
            // This matches the IEEE 754:2019 `minimumMagnitude` function
            //
            // It propagates NaN inputs back to the caller and
            // otherwise returns the input with a lesser magnitude.
            // It treats +0 as greater than -0 as per the specification.

            float ax = Abs(x);
            float ay = Abs(y);

            if ((ax < ay) || float.IsNaN(ax))
            {
                return x;
            }

            if (ax == ay)
            {
                return float.IsNegative(x) ? x : y;
            }

            return y;
        }

        [Intrinsic]
        public static float ReciprocalEstimate(float value) => 1 / value;
        [Intrinsic]
        public static float ReciprocalSqrtEstimate(float value) => 1 / Sqrt(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float value, int digits)
        {
            return Round(value, digits, MidpointRounding.ToEven);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float value, MidpointRounding mode) => (float)Math.Round(value, mode);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float x, int digits, MidpointRounding mode)
        {
            if (digits < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRange_RoundingDigits(nameof(digits));
            }

            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity)
            {
                ThrowHelper.ThrowArgumentException_InvalidEnumValue(mode);
            }

            // Rounding to zero fractional digits is just rounding to an integer, which the dedicated
            // overload does with a single hardware instruction on most platforms.
            if (digits == 0)
            {
                return Round(x, mode);
            }

            // Only finite values with a magnitude below the integer boundary can have a fractional
            // portion to round. All other values (including NaN and Infinity) are returned unchanged;
            // this comparison is naturally false for those cases.
            if (Abs(x) < singleIntegerBoundary)
            {
                // The fast path only handles the small-digit range where `10^digits` is exactly
                // representable; larger counts fall back to the exact arbitrary-precision routine.
                if ((digits > maxFastRoundingDigits) || !Number.TryRoundToDecimalDigitsFast(x, digits, mode, out float rounded))
                {
                    rounded = Number.RoundToDecimalDigits<float>(x, digits, mode);
                }
                x = rounded;
            }

            return x;
        }
        // Upstream's managed scalblnf: MathF's SCALEB_C1, SCALEB_C2 and SCALEB_C3 as literals.
        public static float ScaleB(float x, int n)
        {
            // Implementation based on https://git.musl-libc.org/cgit/musl/tree/src/math/scalblnf.c
            //
            // Performs the calculation x * 2^n efficiently. It constructs a float from 2^n by building
            // the correct biased exponent. If n is greater than the maximum exponent (127) or less than
            // the minimum exponent (-126), adjust x and n to compute correct result.

            float y = x;
            if (n > 127)
            {
                y *= 1.7014118E+38f; // 0x1p127f
                n -= 127;
                if (n > 127)
                {
                    y *= 1.7014118E+38f;
                    n -= 127;
                    if (n > 127)
                    {
                        n = 127;
                    }
                }
            }
            else if (n < -126)
            {
                y *= 1.1754944E-38f * 16777216f; // 0x1p-126f * 0x1p24f
                n += 126 - 24;
                if (n < -126)
                {
                    y *= 1.1754944E-38f * 16777216f;
                    n += 126 - 24;
                    if (n < -126)
                    {
                        n = -126;
                    }
                }
            }

            float u = BitConverter.Int32BitsToSingle(((int)(0x7f + n) << 23));
            return y * u;
        }

        [Intrinsic]
        public static float Sinh(float value) => (float)Math.Sinh(value);
        [Intrinsic]
        public static float Tanh(float value) => (float)Math.Tanh(value);

        public static float Clamp(float value, float minimum, float maximum)
        {
            if (minimum > maximum) throw new ArgumentException();
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(float value)
        {
            if (float.IsNaN(value)) throw new ArithmeticException();
            return value < 0 ? -1 : value > 0 ? 1 : 0;
        }

        private static bool IsNegative(float value) =>
            value < 0 || value == 0 && 1 / value == float.NegativeInfinity;
    }
}
