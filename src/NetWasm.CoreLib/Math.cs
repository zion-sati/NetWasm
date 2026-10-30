// Adapted from dotnet/runtime System.Private.CoreLib Math.cs and MathF.cs.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
// Source reference: dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de.
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>Common mathematical constants and scalar operations.</summary>
    public static partial class Math
    {
        public const double E = 2.718281828459045;
        public const double PI = 3.141592653589793;
        public const double Tau = 6.283185307179586;

        public static double Abs(double value) =>
            BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(value) & 0x7FFFFFFFFFFFFFFFUL);

        public static float Abs(float value) =>
            BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(value) & 0x7FFFFFFFU);
        public static double Ceiling(double value) => CeilingCore(value);
        public static double Floor(double value) => FloorCore(value);
        public static double Truncate(double value) => TruncateCore(value);
        public static double Sqrt(double value) => SqrtCore(value);

        public static short Abs(short value)
        {
            if (value == short.MinValue) throw new OverflowException();
            return value < 0 ? (short)-value : value;
        }

        public static int Abs(int value)
        {
            if (value == int.MinValue) throw new OverflowException();
            return value < 0 ? -value : value;
        }

        public static long Abs(long value)
        {
            if (value == long.MinValue) throw new OverflowException();
            return value < 0 ? -value : value;
        }

        public static nint Abs(nint value)
        {
            if (value == nint.MinValue) throw new OverflowException();
            return value < 0 ? -value : value;
        }

        public static sbyte Abs(sbyte value)
        {
            if (value == sbyte.MinValue) throw new OverflowException();
            return value < 0 ? (sbyte)-value : value;
        }

        public static decimal Abs(decimal value) => decimal.Abs(value);

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
        public static ulong BigMul(uint a, uint b) => (ulong)a * b;

        public static long BigMul(long a, long b, out long low)
        {
            var high = BigMul((ulong)a, (ulong)b, out var unsignedLow);
            low = (long)unsignedLow;
            return (long)high - ((a >> 63) & b) - ((b >> 63) & a);
        }

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


        public static double CopySign(double value, double sign)
        {
            var magnitude = BitConverter.DoubleToUInt64Bits(value) & 0x7FFFFFFFFFFFFFFFUL;
            var signBit = BitConverter.DoubleToUInt64Bits(sign) & 0x8000000000000000UL;
            return BitConverter.UInt64BitsToDouble(magnitude | signBit);
        }

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

        public static (sbyte Quotient, sbyte Remainder) DivRem(sbyte left, sbyte right)
        {
            var quotient = (sbyte)(left / right);
            return (quotient, (sbyte)(left - quotient * right));
        }

        public static (byte Quotient, byte Remainder) DivRem(byte left, byte right)
        {
            var quotient = (byte)(left / right);
            return (quotient, (byte)(left - quotient * right));
        }

        public static (short Quotient, short Remainder) DivRem(short left, short right)
        {
            var quotient = (short)(left / right);
            return (quotient, (short)(left - quotient * right));
        }

        public static (ushort Quotient, ushort Remainder) DivRem(ushort left, ushort right)
        {
            var quotient = (ushort)(left / right);
            return (quotient, (ushort)(left - quotient * right));
        }

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

        public static (uint Quotient, uint Remainder) DivRem(uint left, uint right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

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

        public static (ulong Quotient, ulong Remainder) DivRem(ulong left, ulong right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        public static (nint Quotient, nint Remainder) DivRem(nint left, nint right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

        public static (nuint Quotient, nuint Remainder) DivRem(nuint left, nuint right)
        {
            var quotient = left / right;
            return (quotient, left - quotient * right);
        }

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

        public static double Log10(double value) => Log(value) / 2.302585092994046;
        public static double Log2(double value) => Log(value) / 0.6931471805599453;

        public static byte Max(byte left, byte right) => left >= right ? left : right;
        public static short Max(short left, short right) => left >= right ? left : right;
        public static int Max(int left, int right) => left >= right ? left : right;
        public static long Max(long left, long right) => left >= right ? left : right;
        public static nint Max(nint left, nint right) => left >= right ? left : right;
        public static sbyte Max(sbyte left, sbyte right) => left >= right ? left : right;
        public static ushort Max(ushort left, ushort right) => left >= right ? left : right;
        public static uint Max(uint left, uint right) => left >= right ? left : right;
        public static ulong Max(ulong left, ulong right) => left >= right ? left : right;
        public static nuint Max(nuint left, nuint right) => left >= right ? left : right;
        public static decimal Max(decimal left, decimal right) => decimal.Max(left, right);

        public static double Max(double left, double right)
        {
            if (left != right) return !IsNaN(left) ? left > right ? left : right : left;
            return IsNegative(right) ? left : right;
        }

        public static float Max(float left, float right)
        {
            if (left != right) return !IsNaN(left) ? left > right ? left : right : left;
            return IsNegative(right) ? left : right;
        }

        public static double MaxMagnitude(double left, double right)
        {
            var absoluteLeft = Abs(left);
            var absoluteRight = Abs(right);
            if (absoluteLeft > absoluteRight || IsNaN(absoluteLeft)) return left;
            if (absoluteLeft == absoluteRight) return IsNegative(left) ? right : left;
            return right;
        }

        public static byte Min(byte left, byte right) => left <= right ? left : right;
        public static short Min(short left, short right) => left <= right ? left : right;
        public static int Min(int left, int right) => left <= right ? left : right;
        public static long Min(long left, long right) => left <= right ? left : right;
        public static nint Min(nint left, nint right) => left <= right ? left : right;
        public static sbyte Min(sbyte left, sbyte right) => left <= right ? left : right;
        public static ushort Min(ushort left, ushort right) => left <= right ? left : right;
        public static uint Min(uint left, uint right) => left <= right ? left : right;
        public static ulong Min(ulong left, ulong right) => left <= right ? left : right;
        public static nuint Min(nuint left, nuint right) => left <= right ? left : right;
        public static decimal Min(decimal left, decimal right) => decimal.Min(left, right);

        public static double Min(double left, double right)
        {
            if (left != right) return !IsNaN(left) ? left < right ? left : right : left;
            return IsNegative(left) ? left : right;
        }

        public static float Min(float left, float right)
        {
            if (left != right) return !IsNaN(left) ? left < right ? left : right : left;
            return IsNegative(left) ? left : right;
        }

        public static double MinMagnitude(double left, double right)
        {
            var absoluteLeft = Abs(left);
            var absoluteRight = Abs(right);
            if (absoluteLeft < absoluteRight || IsNaN(absoluteLeft)) return left;
            if (absoluteLeft == absoluteRight) return IsNegative(left) ? left : right;
            return right;
        }

        public static double ReciprocalEstimate(double value) => 1 / value;
        public static double ReciprocalSqrtEstimate(double value) => 1 / Sqrt(value);

        public static decimal Round(decimal value) => decimal.Round(value);
        public static decimal Round(decimal value, int decimals) => decimal.Round(value, decimals);
        public static decimal Round(decimal value, MidpointRounding mode) => decimal.Round(value, mode);
        public static decimal Round(decimal value, int decimals, MidpointRounding mode) => decimal.Round(value, decimals, mode);
        public static double Round(double value) => RoundCore(value);
        public static double Round(double value, int digits) => Round(value, digits, MidpointRounding.ToEven);
        public static double Round(double value, MidpointRounding mode) => RoundMode(value, mode);
        public static double Round(double value, int digits, MidpointRounding mode)
        {
            if (digits < 0) throw new ArgumentOutOfRangeException();
            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity) throw new ArgumentException();
            if (digits == 0) return RoundMode(value, mode);
            if (Abs(value) >= 1e16 || IsNaN(value) || IsInfinity(value)) return value;
            if (digits <= 15)
            {
                var power = Pow10(digits);
                return RoundMode(value * power, mode) / power;
            }
            return Number.RoundToDecimalDigits(value, digits, mode);
        }

        public static double ScaleB(double value, int n)
        {
            if (value == 0 || IsNaN(value) || IsInfinity(value) || n == 0) return value;
            var scaled = value;
            if (n > 1023)
            {
                scaled *= 8.98846567431158E+307;
                n -= 1023;
                if (n > 1023)
                {
                    scaled *= 8.98846567431158E+307;
                    n -= 1023;
                    if (n > 1023) n = 1023;
                }
            }
            else if (n < -1022)
            {
                scaled *= 2.2250738585072014E-308 * 9007199254740992;
                n += 969;
                if (n < -1022)
                {
                    scaled *= 2.2250738585072014E-308 * 9007199254740992;
                    n += 969;
                    if (n < -1022) n = -1022;
                }
            }
            var power = BitConverter.UInt64BitsToDouble((ulong)(0x3ff + n) << 52);
            return scaled * power;
        }

        public static int Sign(short value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(long value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(nint value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(sbyte value) => value < 0 ? -1 : value > 0 ? 1 : 0;
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

        private static double Pow10(int digits)
        {
            var result = 1.0;
            while (digits-- > 0) result *= 10;
            return result;
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
        public const float E = 2.7182817f;
        public const float PI = 3.1415927f;
        public const float Tau = 6.2831855f;

        public static float Abs(float value) => Math.Abs(value);
        public static float Ceiling(float value) => (float)Math.Ceiling(value);
        public static float Floor(float value) => (float)Math.Floor(value);
        public static float Truncate(float value) => (float)Math.Truncate(value);
        public static float Round(float value) => (float)Math.Round(value);
        public static float Sqrt(float value) => (float)Math.Sqrt(value);

        public static float Acos(float value) => (float)Math.Acos(value);
        public static float Acosh(float value) => (float)Math.Acosh(value);
        public static float Asin(float value) => (float)Math.Asin(value);
        public static float Asinh(float value) => (float)Math.Asinh(value);
        public static float Atan(float value) => (float)Math.Atan(value);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
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
        public static float Cbrt(float value) => (float)Math.Cbrt(value);
        public static float CopySign(float value, float sign) =>
            BitConverter.UInt32BitsToSingle(
                (BitConverter.SingleToUInt32Bits(value) & 0x7FFFFFFFU) |
                (BitConverter.SingleToUInt32Bits(sign) & 0x80000000U));
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
        public static float Log10(float value) => (float)Math.Log10(value);

        public static float Min(float left, float right)
        {
            if (left != right) return !float.IsNaN(left) ? left < right ? left : right : left;
            return IsNegative(left) ? left : right;
        }

        public static float Max(float left, float right)
        {
            if (left != right) return !float.IsNaN(left) ? left > right ? left : right : left;
            return IsNegative(right) ? left : right;
        }

        public static float MaxMagnitude(float left, float right)
        {
            var absoluteLeft = Abs(left);
            var absoluteRight = Abs(right);
            if (absoluteLeft > absoluteRight || float.IsNaN(absoluteLeft)) return left;
            if (absoluteLeft == absoluteRight) return IsNegative(left) ? right : left;
            return right;
        }

        public static float MinMagnitude(float left, float right)
        {
            var absoluteLeft = Abs(left);
            var absoluteRight = Abs(right);
            if (absoluteLeft < absoluteRight || float.IsNaN(absoluteLeft)) return left;
            if (absoluteLeft == absoluteRight) return IsNegative(left) ? left : right;
            return right;
        }

        public static float ReciprocalEstimate(float value) => 1 / value;
        public static float ReciprocalSqrtEstimate(float value) => 1 / Sqrt(value);
        public static float Round(float value, int digits)
        {
            return Round(value, digits, MidpointRounding.ToEven);
        }
        public static float Round(float value, MidpointRounding mode) => (float)Math.Round(value, mode);
        public static float Round(float value, int digits, MidpointRounding mode)
        {
            if (digits < 0) throw new ArgumentOutOfRangeException();
            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity) throw new ArgumentException();
            if (digits == 0) return Round(value, mode);
            if (Abs(value) >= 8388608 || float.IsNaN(value) || float.IsInfinity(value)) return value;
            if (digits <= 6)
            {
                var power = 1.0f;
                for (var index = 0; index < digits; index++) power *= 10;
                return (float)(Math.Round((double)value * power, mode) / power);
            }
            return Number.RoundToDecimalDigits(value, digits, mode);
        }
        public static float ScaleB(float value, int n) => (float)Math.ScaleB(value, n);

        public static float Sinh(float value) => (float)Math.Sinh(value);
        public static float Tanh(float value) => (float)Math.Tanh(value);

        public static float Clamp(float value, float minimum, float maximum)
        {
            if (minimum > maximum) throw new ArgumentException();
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        public static int Sign(float value)
        {
            if (float.IsNaN(value)) throw new ArithmeticException();
            return value < 0 ? -1 : value > 0 ? 1 : 0;
        }

        private static bool IsNegative(float value) =>
            value < 0 || value == 0 && 1 / value == float.NegativeInfinity;
    }
}
