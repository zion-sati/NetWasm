// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// This implementation is adapted from dotnet/runtime System.Runtime.Numerics
// BigInteger and BigIntegerCalculator at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. The NetWasm profile retains the
// invariant, managed scalar algorithms and omits culture, serialization,
// reflection, dynamic, threading, and SIMD-only branches.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace System.Numerics
{
    /// <summary>Represents an arbitrarily large signed integer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct BigInteger : IComparable, IComparable<BigInteger>, IEquatable<BigInteger>,
        IFormattable, IParsable<BigInteger>, ISpanFormattable, ISpanParsable<BigInteger>,
        IUtf8SpanFormattable, IUtf8SpanParsable<BigInteger>, IBinaryInteger<BigInteger>, ISignedNumber<BigInteger>
    {
        private readonly int _sign;
        private readonly uint[]? _bits;

        private BigInteger(int sign, uint[] bits)
        {
            var length = bits.Length;
            while (length > 0 && bits[length - 1] == 0) length--;
            if (length == 0)
            {
                _sign = 0;
                _bits = null;
            }
            else if (length == bits.Length)
            {
                _sign = sign < 0 ? -1 : 1;
                _bits = bits;
            }
            else
            {
                var normalized = new uint[length];
                for (var index = 0; index < length; index++) normalized[index] = bits[index];
                _sign = sign < 0 ? -1 : 1;
                _bits = normalized;
            }
        }

        public BigInteger(int value) => this = FromInt64(value);
        public BigInteger(long value) => this = FromInt64(value);
        public BigInteger(uint value) => this = FromUInt64(value);
        public BigInteger(ulong value) => this = FromUInt64(value);
        public BigInteger(byte value) => this = FromUInt64(value);
        public BigInteger(sbyte value) => this = FromInt64(value);
        public BigInteger(short value) => this = FromInt64(value);
        public BigInteger(ushort value) => this = FromUInt64(value);
        public BigInteger(char value) => this = FromUInt64(value);
        public BigInteger(nint value) => this = FromInt64((long)value);
        public BigInteger(nuint value) => this = FromUInt64((ulong)value);
        public BigInteger(double value) => this = FromDouble(value, throwOnOverflow: false);
        public BigInteger(float value) => this = FromDouble(value, throwOnOverflow: false);
        public BigInteger(Half value) => this = FromDouble((double)value, throwOnOverflow: false);
        public BigInteger(decimal value) => this = FromDecimal(value);

        public BigInteger(byte[] value) : this(value is null ? throw new ArgumentNullException(nameof(value)) : value.AsSpan(), false, false) { }

        public BigInteger(ReadOnlySpan<byte> value, bool isUnsigned = false, bool isBigEndian = false)
        {
            if (value.Length == 0)
            {
                _sign = 0;
                _bits = null;
                return;
            }

            var bytes = new byte[value.Length];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = value[isBigEndian ? value.Length - index - 1 : index];
            }

            var negative = !isUnsigned && (bytes[^1] & 0x80) != 0;
            if (negative)
            {
                var carry = 1;
                for (var index = 0; index < bytes.Length; index++)
                {
                    bytes[index] = unchecked((byte)~bytes[index]);
                    var sum = bytes[index] + carry;
                    bytes[index] = (byte)sum;
                    carry = sum >> 8;
                }
            }

            _bits = BytesToMagnitude(bytes);
            _sign = _bits is null ? 0 : negative ? -1 : 1;
        }

        public static BigInteger Zero => default;
        public static BigInteger One => new BigInteger(1);
        public static BigInteger MinusOne => new BigInteger(-1);
        public bool IsZero => _sign == 0;
        public bool IsOne => _sign == 1 && _bits!.Length == 1 && _bits[0] == 1;
        public bool IsEven => _sign == 0 || (_bits![0] & 1) == 0;
        public bool IsPowerOfTwo => _sign > 0 && IsMagnitudePowerOfTwo(_bits!);
        public int Sign => _sign;

        public static BigInteger Abs(BigInteger value) => value._sign < 0 ? -value : value;
        public static BigInteger Negate(BigInteger value) => -value;
        public static BigInteger Add(BigInteger left, BigInteger right) => left + right;
        public static BigInteger Subtract(BigInteger left, BigInteger right) => left - right;
        public static BigInteger Multiply(BigInteger left, BigInteger right) => left * right;
        public static BigInteger Divide(BigInteger left, BigInteger right) => left / right;
        public static BigInteger Remainder(BigInteger left, BigInteger right) => left % right;

        public static BigInteger operator +(BigInteger left, BigInteger right)
        {
            if (left._sign == 0) return right;
            if (right._sign == 0) return left;
            if (left._sign == right._sign) return new BigInteger(left._sign, AddMagnitude(left._bits!, right._bits!));
            var comparison = CompareMagnitude(left._bits!, right._bits!);
            if (comparison == 0) return default;
            return comparison > 0
                ? new BigInteger(left._sign, SubtractMagnitude(left._bits!, right._bits!))
                : new BigInteger(right._sign, SubtractMagnitude(right._bits!, left._bits!));
        }

        public static BigInteger operator -(BigInteger left, BigInteger right) => left + -right;
        public static BigInteger operator -(BigInteger value) => value._sign == 0 ? value : new BigInteger(-value._sign, value._bits!);
        public static BigInteger operator +(BigInteger value) => value;
        public static BigInteger operator *(BigInteger left, BigInteger right)
        {
            if (left._sign == 0 || right._sign == 0) return default;
            return new BigInteger(left._sign == right._sign ? 1 : -1, MultiplyMagnitude(left._bits!, right._bits!));
        }

        public static BigInteger operator /(BigInteger left, BigInteger right)
        {
            return DivRem(left, right, out _);
        }

        public static BigInteger operator %(BigInteger left, BigInteger right)
        {
            DivRem(left, right, out var remainder);
            return remainder;
        }

        public static BigInteger DivRem(BigInteger dividend, BigInteger divisor, out BigInteger remainder)
        {
            if (divisor._sign == 0) throw new DivideByZeroException();
            if (dividend._sign == 0)
            {
                remainder = default;
                return default;
            }
            var comparison = CompareMagnitude(dividend._bits!, divisor._bits!);
            if (comparison < 0)
            {
                remainder = dividend;
                return default;
            }
            if (comparison == 0)
            {
                remainder = default;
                return new BigInteger(dividend._sign == divisor._sign ? 1 : -1, new uint[] { 1 });
            }

            DivideMagnitude(dividend._bits!, divisor._bits!, out var quotientMagnitude, out var remainderMagnitude);
            var quotient = new BigInteger(dividend._sign == divisor._sign ? 1 : -1, quotientMagnitude);
            remainder = new BigInteger(dividend._sign, remainderMagnitude);
            return quotient;
        }

        public static (BigInteger Quotient, BigInteger Remainder) DivRem(BigInteger left, BigInteger right)
        {
            var quotient = DivRem(left, right, out var remainder);
            return (quotient, remainder);
        }

        public static BigInteger GreatestCommonDivisor(BigInteger left, BigInteger right)
        {
            left = Abs(left);
            right = Abs(right);
            while (!right.IsZero)
            {
                left = left % right;
                (left, right) = (right, left);
            }
            return left;
        }

        public static BigInteger Pow(BigInteger value, int exponent)
        {
            if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent));
            var result = One;
            while (exponent != 0)
            {
                if ((exponent & 1) != 0) result *= value;
                value *= value;
                exponent >>= 1;
            }
            return result;
        }

        public static BigInteger ModPow(BigInteger value, BigInteger exponent, BigInteger modulus)
        {
            if (modulus.IsZero) throw new DivideByZeroException();
            if (exponent._sign < 0) throw new ArgumentOutOfRangeException(nameof(exponent));
            var result = One % modulus;
            value %= modulus;
            while (!exponent.IsZero)
            {
                if (!exponent.IsEven) result = (result * value) % modulus;
                exponent >>= 1;
                value = (value * value) % modulus;
            }
            return result;
        }

        public static BigInteger operator &(BigInteger left, BigInteger right) => Bitwise(left, right, '&');
        public static BigInteger operator |(BigInteger left, BigInteger right) => Bitwise(left, right, '|');
        public static BigInteger operator ^(BigInteger left, BigInteger right) => Bitwise(left, right, '^');
        public static BigInteger operator ~(BigInteger value) => Bitwise(value, MinusOne, '^');

        public static BigInteger operator <<(BigInteger value, int shift)
        {
            if (shift < 0)
            {
                if (shift == int.MinValue) return value._sign < 0 ? MinusOne : Zero;
                return value >> -shift;
            }
            if (value.IsZero || shift == 0) return value;
            return new BigInteger(value._sign, ShiftLeftMagnitude(value._bits!, shift));
        }

        public static BigInteger operator >>(BigInteger value, int shift)
        {
            if (shift < 0)
            {
                if (shift == int.MinValue) return value << int.MaxValue << 1;
                return value << -shift;
            }
            if (value.IsZero || shift == 0) return value;
            var magnitude = ShiftRightMagnitude(value._bits!, shift, out var discarded);
            if (value._sign > 0) return new BigInteger(1, magnitude);
            if (discarded) magnitude = AddMagnitude(magnitude, new uint[] { 1 });
            return new BigInteger(-1, magnitude);
        }

        public static BigInteger operator >>>(BigInteger value, int shift)
        {
            if (shift < 0)
            {
                if (shift == int.MinValue) return value << int.MaxValue << 1;
                return value << -shift;
            }
            if (value.IsZero || shift == 0) return value;

            // The runtime defines unsigned right shift over the finite two's-complement
            // representation. Use a 32-bit word ring for small values and preserve the
            // sign extension of larger negative values before shifting it away.
            var width = RotationWidth(value);
            if (shift >= width)
            {
                return value._sign < 0 ? MinusOne : Zero;
            }
            var words = value._sign < 0 ? value.ToTwosComplement(width) : CopyWords(value._bits!, width);
            words = ShiftRightWords(words, shift);
            return new BigInteger(1, words);
        }
        public static BigInteger operator ++(BigInteger value) => value + One;
        public static BigInteger operator --(BigInteger value) => value - One;

        public static bool operator ==(BigInteger left, BigInteger right) => left.Equals(right);
        public static bool operator !=(BigInteger left, BigInteger right) => !left.Equals(right);
        public static bool operator <(BigInteger left, BigInteger right) => left.CompareTo(right) < 0;
        public static bool operator <=(BigInteger left, BigInteger right) => left.CompareTo(right) <= 0;
        public static bool operator >(BigInteger left, BigInteger right) => left.CompareTo(right) > 0;
        public static bool operator >=(BigInteger left, BigInteger right) => left.CompareTo(right) >= 0;
        public static bool operator ==(BigInteger left, long right) => left.Equals(right);
        public static bool operator !=(BigInteger left, long right) => !left.Equals(right);
        public static bool operator <(BigInteger left, long right) => left.CompareTo(right) < 0;
        public static bool operator <=(BigInteger left, long right) => left.CompareTo(right) <= 0;
        public static bool operator >(BigInteger left, long right) => left.CompareTo(right) > 0;
        public static bool operator >=(BigInteger left, long right) => left.CompareTo(right) >= 0;
        public static bool operator ==(long left, BigInteger right) => right.Equals(left);
        public static bool operator !=(long left, BigInteger right) => !right.Equals(left);
        public static bool operator <(long left, BigInteger right) => right.CompareTo(left) > 0;
        public static bool operator <=(long left, BigInteger right) => right.CompareTo(left) >= 0;
        public static bool operator >(long left, BigInteger right) => right.CompareTo(left) < 0;
        public static bool operator >=(long left, BigInteger right) => right.CompareTo(left) <= 0;
        public static bool operator ==(BigInteger left, ulong right) => left.Equals(right);
        public static bool operator !=(BigInteger left, ulong right) => !left.Equals(right);
        public static bool operator <(BigInteger left, ulong right) => left.CompareTo(right) < 0;
        public static bool operator <=(BigInteger left, ulong right) => left.CompareTo(right) <= 0;
        public static bool operator >(BigInteger left, ulong right) => left.CompareTo(right) > 0;
        public static bool operator >=(BigInteger left, ulong right) => left.CompareTo(right) >= 0;
        public static bool operator ==(ulong left, BigInteger right) => right.Equals(left);
        public static bool operator !=(ulong left, BigInteger right) => !right.Equals(left);
        public static bool operator <(ulong left, BigInteger right) => right.CompareTo(left) > 0;
        public static bool operator <=(ulong left, BigInteger right) => right.CompareTo(left) >= 0;
        public static bool operator >(ulong left, BigInteger right) => right.CompareTo(left) < 0;
        public static bool operator >=(ulong left, BigInteger right) => right.CompareTo(left) <= 0;

        public static int Compare(BigInteger left, BigInteger right) => left.CompareTo(right);
        public int CompareTo(long other) => CompareTo((BigInteger)other);
        public int CompareTo(ulong other) => CompareTo((BigInteger)other);
        public bool Equals(long other) => Equals((BigInteger)other);
        public bool Equals(ulong other) => Equals((BigInteger)other);

        public int CompareTo(BigInteger other)
        {
            if (_sign != other._sign) return _sign < other._sign ? -1 : 1;
            if (_sign == 0) return 0;
            var result = CompareMagnitude(_bits!, other._bits!);
            return _sign < 0 ? -result : result;
        }

        public int CompareTo(object? obj)
        {
            if (obj is null) return 1;
            if (obj is BigInteger other) return CompareTo(other);
            throw new ArgumentException("Object must be a BigInteger.", nameof(obj));
        }

        public bool Equals(BigInteger other) => _sign == other._sign && (_sign == 0 || MagnitudesEqual(_bits!, other._bits!));
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is BigInteger other && Equals(other);
        public override int GetHashCode()
        {
            var hash = _sign;
            if (_bits is not null)
            {
                for (var index = 0; index < _bits.Length; index++) hash = unchecked(hash * 31 + (int)_bits[index]);
            }
            return hash;
        }

        public long GetBitLength()
        {
            if (_sign == 0) return 0;
            var bits = BitLength(_bits!);
            if (_sign < 0 && IsMagnitudePowerOfTwo(_bits!)) return bits - 1;
            return bits;
        }

        public int GetByteCount(bool isUnsigned = false) => ToByteArray(isUnsigned, false).Length;
        public int GetByteCount() => GetByteCount(false);
        public int GetShortestBitLength() => checked((int)(_sign < 0 ? GetBitLength() + 1 : GetBitLength()));
        public byte[] ToByteArray() => ToByteArray(false, false);
        public byte[] ToByteArray(bool isUnsigned, bool isBigEndian = false)
        {
            if (isUnsigned && _sign < 0) throw new OverflowException();
            var bytes = ToLittleEndianBytes(isUnsigned);
            if (!isBigEndian) return bytes;
            var reversed = new byte[bytes.Length];
            for (var index = 0; index < bytes.Length; index++) reversed[index] = bytes[bytes.Length - index - 1];
            return reversed;
        }

        public bool TryWriteBytes(Span<byte> destination, out int bytesWritten, bool isUnsigned = false, bool isBigEndian = false)
        {
            var bytes = ToByteArray(isUnsigned, isBigEndian);
            if (destination.Length < bytes.Length)
            {
                bytesWritten = 0;
                return false;
            }
            for (var index = 0; index < bytes.Length; index++) destination[index] = bytes[index];
            bytesWritten = bytes.Length;
            return true;
        }

        public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten) => TryWriteBytes(destination, out bytesWritten, false, true);
        public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten) => TryWriteBytes(destination, out bytesWritten, false, false);

        public override string ToString() => Format(null);
        public string ToString(IFormatProvider? provider) => Format(null);
        public string ToString(string? format) => Format(format);
        public string ToString(string? format, IFormatProvider? provider) => Format(format);

        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
        {
            var text = Format(format.Length == 0 ? null : FormatSpan(format));
            if (text.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }
            for (var index = 0; index < text.Length; index++) destination[index] = text[index];
            charsWritten = text.Length;
            return true;
        }

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
        {
            var text = Format(format.Length == 0 ? null : FormatSpan(format));
            if (text.Length > utf8Destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            for (var index = 0; index < text.Length; index++) utf8Destination[index] = (byte)text[index];
            bytesWritten = text.Length;
            return true;
        }

        public static BigInteger Parse(string s) => Parse(s, NumberStyles.Integer, null);
        public static BigInteger Parse(string s, NumberStyles style) => Parse(s, style, null);
        public static BigInteger Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);
        public static BigInteger Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s is null) throw new ArgumentNullException(nameof(s));
            if (!TryParse(s.AsSpan(), style, provider, out var result)) throw new FormatException();
            return result;
        }

        public static BigInteger Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            if (!TryParse(s, style, provider, out var result)) throw new FormatException();
            return result;
        }

        public static BigInteger Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Integer, provider);

        public static BigInteger Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = NumberStyles.Integer, IFormatProvider? provider = null)
        {
            if (!TryParse(utf8Text, style, provider, out var result)) throw new FormatException();
            return result;
        }

        public static BigInteger Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, NumberStyles.Integer, provider);

        public static bool TryParse(string? s, out BigInteger result) => TryParse(s, NumberStyles.Integer, null, out result);
        public static bool TryParse(string? s, IFormatProvider? provider, out BigInteger result) => TryParse(s, NumberStyles.Integer, provider, out result);
        public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out BigInteger result)
        {
            result = default;
            return s is not null && TryParse(s.AsSpan(), style, provider, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out BigInteger result) => TryParse(s, NumberStyles.Integer, null, out result);
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out BigInteger result) => TryParse(s, NumberStyles.Integer, provider, out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out BigInteger result)
        {
            result = default;
            var hex = (style & NumberStyles.AllowHexSpecifier) != 0;
            var binary = (style & NumberStyles.AllowBinarySpecifier) != 0;
            var unsupported = style & ~(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign | NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier);
            if (hex && binary || unsupported != 0) return false;
            var start = 0;
            var end = s.Length;
            if ((style & NumberStyles.AllowLeadingWhite) != 0) while (start < end && IsWhite(s[start])) start++;
            if ((style & NumberStyles.AllowTrailingWhite) != 0) while (end > start && IsWhite(s[end - 1])) end--;
            if (start == end) return false;
            var negative = false;
            if (!hex && !binary && (style & NumberStyles.AllowLeadingSign) != 0 && (s[start] == '+' || s[start] == '-'))
            {
                negative = s[start] == '-';
                start++;
            }
            if (start == end) return false;
            var radix = binary ? 2u : hex ? 16u : 10u;
            var magnitude = default(BigInteger);
            for (var index = start; index < end; index++)
            {
                var digit = Digit(s[index]);
                if (digit < 0 || (uint)digit >= radix) return false;
                magnitude = magnitude * radix + (uint)digit;
            }
            result = negative ? -magnitude : magnitude;
            return true;
        }

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out BigInteger result) => TryParse(utf8Text, NumberStyles.Integer, null, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out BigInteger result) => TryParse(utf8Text, NumberStyles.Integer, provider, out result);
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out BigInteger result)
        {
            var chars = new char[utf8Text.Length];
            for (var index = 0; index < chars.Length; index++)
            {
                if (utf8Text[index] > 0x7F) { result = default; return false; }
                chars[index] = (char)utf8Text[index];
            }
            return TryParse(chars.AsSpan(), style, provider, out result);
        }

        public static explicit operator BigInteger(decimal value) => FromDecimal(value);
        public static explicit operator BigInteger(double value) => FromDouble(value, throwOnOverflow: false);
        public static explicit operator checked BigInteger(double value) => FromDouble(value, throwOnOverflow: true);
        public static explicit operator BigInteger(float value) => FromDouble(value, throwOnOverflow: false);
        public static explicit operator checked BigInteger(float value) => FromDouble(value, throwOnOverflow: true);
        public static explicit operator BigInteger(Half value) => FromDouble((double)value, throwOnOverflow: false);
        public static explicit operator BigInteger(BFloat16 value) => FromDouble((double)value, throwOnOverflow: false);
        public static explicit operator BigInteger(Complex value)
        {
            if (value.Imaginary != 0) throw new OverflowException();
            return FromDouble(value.Real, throwOnOverflow: false);
        }
        public static implicit operator BigInteger(byte value) => new BigInteger(value);
        public static implicit operator BigInteger(sbyte value) => new BigInteger(value);
        public static implicit operator BigInteger(short value) => new BigInteger(value);
        public static implicit operator BigInteger(ushort value) => new BigInteger(value);
        public static implicit operator BigInteger(int value) => new BigInteger(value);
        public static implicit operator BigInteger(uint value) => new BigInteger(value);
        public static implicit operator BigInteger(long value) => new BigInteger(value);
        public static implicit operator BigInteger(ulong value) => new BigInteger(value);
        public static implicit operator BigInteger(char value) => new BigInteger(value);
        public static implicit operator BigInteger(nint value) => new BigInteger(value);
        public static implicit operator BigInteger(nuint value) => new BigInteger(value);
        public static implicit operator BigInteger(Int128 value) => Parse(value.ToString(), NumberStyles.Integer, null);
        public static implicit operator BigInteger(UInt128 value) => Parse(value.ToString(), NumberStyles.Integer, null);

        public static explicit operator int(BigInteger value) => unchecked((int)value.ToUInt64());
        public static explicit operator uint(BigInteger value) => unchecked((uint)value.ToUInt64());
        public static explicit operator long(BigInteger value) => unchecked((long)value.ToUInt64());
        public static explicit operator ulong(BigInteger value) => value.ToUInt64();
        public static explicit operator short(BigInteger value) => unchecked((short)value.ToUInt64());
        public static explicit operator ushort(BigInteger value) => unchecked((ushort)value.ToUInt64());
        public static explicit operator byte(BigInteger value) => unchecked((byte)value.ToUInt64());
        public static explicit operator char(BigInteger value) => unchecked((char)value.ToUInt64());
        public static explicit operator sbyte(BigInteger value) => unchecked((sbyte)value.ToUInt64());
        public static explicit operator nint(BigInteger value) => (nint)(long)value;
        public static explicit operator nuint(BigInteger value) => (nuint)value.ToUInt64();
        public static explicit operator double(BigInteger value) => value.ToDouble();
        public static explicit operator float(BigInteger value) => (float)value.ToDouble();
        public static explicit operator Half(BigInteger value) => (Half)value.ToDouble();
        public static explicit operator BFloat16(BigInteger value) => (BFloat16)value.ToDouble();
        public static explicit operator decimal(BigInteger value) => decimal.Parse(value.ToString(), NumberStyles.Integer, null);
        public static explicit operator Int128(BigInteger value) => Int128.Parse(value.ToString());
        public static explicit operator UInt128(BigInteger value) => UInt128.Parse(value.ToString());

        public static bool IsEvenInteger(BigInteger value) => value.IsEven;
        public static bool IsOddInteger(BigInteger value) => !value.IsEven;
        public static bool IsNegative(BigInteger value) => value._sign < 0;
        public static bool IsPositive(BigInteger value) => value._sign >= 0;
        public static bool IsPow2(BigInteger value) => value.IsPowerOfTwo;
        public static BigInteger Log2(BigInteger value)
        {
            if (value._sign <= 0) throw new ArithmeticException();
            return value.GetBitLength() - 1;
        }
        public static BigInteger LeadingZeroCount(BigInteger value)
        {
            if (value._bits is null) return BitOperations.LeadingZeroCount((uint)value._sign);
            return value._sign < 0 ? 0 : BitOperations.LeadingZeroCount(value._bits[^1]) & 31;
        }
        public static BigInteger PopCount(BigInteger value)
        {
            var count = 0;
            var bits = value._bits;
            if (bits is not null && value._sign > 0)
            {
                for (var index = 0; index < bits.Length; index++) count += BitOperations.PopCount(bits[index]);
            }
            else if (bits is not null)
            {
                if (bits.Length == 1)
                {
                    return BitOperations.PopCount(unchecked(0u - bits[0]));
                }

                var firstNonZero = 0;
                while (firstNonZero < bits.Length && bits[firstNonZero] == 0) firstNonZero++;
                if (firstNonZero < bits.Length)
                {
                    count += BitOperations.PopCount(~bits[firstNonZero] + 1);
                    for (var index = firstNonZero + 1; index < bits.Length; index++) count += BitOperations.PopCount(~bits[index]);
                }
            }
            return count;
        }
        public static BigInteger TrailingZeroCount(BigInteger value)
        {
            if (value.IsZero) return 0;
            var count = 0;
            for (var index = 0; index < value._bits!.Length; index++)
            {
                if (value._bits[index] == 0) { count += 32; continue; }
                count += BitOperations.TrailingZeroCount(value._bits[index]);
                break;
            }
            return count;
        }
        public static BigInteger Max(BigInteger left, BigInteger right) => left >= right ? left : right;
        public static BigInteger Min(BigInteger left, BigInteger right) => left <= right ? left : right;
        public static BigInteger MaxMagnitude(BigInteger left, BigInteger right) => Abs(left) >= Abs(right) ? left : right;
        public static BigInteger MinMagnitude(BigInteger left, BigInteger right) => Abs(left) <= Abs(right) ? left : right;
        public static BigInteger CopySign(BigInteger value, BigInteger sign) => IsNegative(value) == IsNegative(sign) ? value : -value;
        public static BigInteger Clamp(BigInteger value, BigInteger min, BigInteger max) => value < min ? min : value > max ? max : value;
        public static double Log(BigInteger value) => Math.Log((double)value);
        public static double Log(BigInteger value, double baseValue) => Math.Log((double)value, baseValue);
        public static double Log10(BigInteger value) => Math.Log10((double)value);

        public static BigInteger CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther>
        {
            if (TryConvertFrom(value, out var result)) return result;
            throw new NotSupportedException();
        }
        public static BigInteger CreateSaturating<TOther>(TOther value) where TOther : INumberBase<TOther> => CreateChecked(value);
        public static BigInteger CreateTruncating<TOther>(TOther value) where TOther : INumberBase<TOther> => CreateChecked(value);

        public static BigInteger RotateLeft(BigInteger value, int rotateAmount) => Rotate(value, rotateAmount);
        public static BigInteger RotateRight(BigInteger value, int rotateAmount) => Rotate(value, -(long)rotateAmount);

        public static bool TryParsePartial(string? s, NumberStyles style, IFormatProvider? provider, out BigInteger result, out int charsConsumed)
        {
            if (s is null)
            {
                result = default;
                charsConsumed = 0;
                return false;
            }
            return TryParsePartial(s.AsSpan(), style, provider, out result, out charsConsumed);
        }

        public static bool TryParsePartial(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out BigInteger result, out int charsConsumed)
        {
            if (!TryGetPartialEnd(s, style, out charsConsumed))
            {
                result = default;
                charsConsumed = 0;
                return false;
            }
            return TryParse(s[..charsConsumed], style, provider, out result);
        }

        public static bool TryParsePartial(ReadOnlySpan<byte> s, NumberStyles style, IFormatProvider? provider, out BigInteger result, out int bytesConsumed)
        {
            var chars = new char[s.Length];
            for (var index = 0; index < chars.Length; index++) chars[index] = (char)s[index];
            return TryParsePartial(chars.AsSpan(), style, provider, out result, out bytesConsumed);
        }

        static int INumberBase<BigInteger>.Radix => 2;
        static BigInteger IAdditiveIdentity<BigInteger, BigInteger>.AdditiveIdentity => Zero;
        static BigInteger IMultiplicativeIdentity<BigInteger, BigInteger>.MultiplicativeIdentity => One;
        static BigInteger ISignedNumber<BigInteger>.NegativeOne => MinusOne;
        static bool INumberBase<BigInteger>.IsCanonical(BigInteger value) => true;
        static bool INumberBase<BigInteger>.IsComplexNumber(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsFinite(BigInteger value) => true;
        static bool INumberBase<BigInteger>.IsImaginaryNumber(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsInfinity(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsInteger(BigInteger value) => true;
        static bool INumberBase<BigInteger>.IsNaN(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsNegativeInfinity(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsNormal(BigInteger value) => !value.IsZero;
        static bool INumberBase<BigInteger>.IsPositiveInfinity(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsRealNumber(BigInteger value) => true;
        static bool INumberBase<BigInteger>.IsSubnormal(BigInteger value) => false;
        static bool INumberBase<BigInteger>.IsZero(BigInteger value) => value.IsZero;
        static BigInteger INumberBase<BigInteger>.MaxMagnitudeNumber(BigInteger x, BigInteger y) => MaxMagnitude(x, y);
        static BigInteger INumberBase<BigInteger>.MinMagnitudeNumber(BigInteger x, BigInteger y) => MinMagnitude(x, y);
        static BigInteger IBinaryInteger<BigInteger>.Log10(BigInteger value) => (BigInteger)Log10(value);
        static BigInteger INumber<BigInteger>.MaxNumber(BigInteger x, BigInteger y) => Max(x, y);
        static BigInteger INumber<BigInteger>.MinNumber(BigInteger x, BigInteger y) => Min(x, y);
        static BigInteger INumberBase<BigInteger>.MultiplyAddEstimate(BigInteger left, BigInteger right, BigInteger addend) => left * right + addend;
        static int INumber<BigInteger>.Sign(BigInteger value) => value.Sign;

        static bool INumberBase<BigInteger>.TryConvertFromChecked<TOther>(TOther value, out BigInteger result) => TryConvertFrom(value, out result);
        static bool INumberBase<BigInteger>.TryConvertFromSaturating<TOther>(TOther value, out BigInteger result) => TryConvertFrom(value, out result);
        static bool INumberBase<BigInteger>.TryConvertFromTruncating<TOther>(TOther value, out BigInteger result) => TryConvertFrom(value, out result);
        static bool INumberBase<BigInteger>.TryConvertToChecked<TOther>(BigInteger value, [MaybeNullWhen(false)] out TOther result) => TryConvertTo(value, out result);
        static bool INumberBase<BigInteger>.TryConvertToSaturating<TOther>(BigInteger value, [MaybeNullWhen(false)] out TOther result) => TryConvertTo(value, out result);
        static bool INumberBase<BigInteger>.TryConvertToTruncating<TOther>(BigInteger value, [MaybeNullWhen(false)] out TOther result) => TryConvertTo(value, out result);

        static bool IBinaryInteger<BigInteger>.TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out BigInteger value)
        {
            value = new BigInteger(source, isUnsigned, true);
            return true;
        }
        static bool IBinaryInteger<BigInteger>.TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out BigInteger value)
        {
            value = new BigInteger(source, isUnsigned, false);
            return true;
        }

        private static string FormatSpan(ReadOnlySpan<char> format)
        {
            var chars = new char[format.Length];
            for (var index = 0; index < chars.Length; index++) chars[index] = format[index];
            return String.Create(chars);
        }

        private string Format(string? format)
        {
            var symbol = format is null || format.Length == 0 ? 'G' : format[0];
            var upper = symbol == 'X';
            var radix = symbol is 'X' or 'x' ? 16 : symbol is 'B' or 'b' ? 2 : 10;
            if (radix == 10 && symbol is not ('G' or 'g' or 'D' or 'd')) throw new FormatException();
            var precision = -1;
            if (format is not null && format.Length > 1)
            {
                precision = 0;
                for (var formatIndex = 1; formatIndex < format.Length; formatIndex++)
                {
                    var digit = format[formatIndex];
                    if (digit < '0' || digit > '9') throw new FormatException();
                    precision = checked(precision * 10 + digit - '0');
                }
            }
            var negative = _sign < 0;
            var magnitude = _bits;
            if (magnitude is null)
            {
                if (precision <= 1) return "0";
                var zeroes = new char[precision];
                for (var zeroIndex = 0; zeroIndex < zeroes.Length; zeroIndex++) zeroes[zeroIndex] = '0';
                return String.Create(zeroes);
            }
            var naturalCapacity = radix == 10 ? checked(BitLength(magnitude) * 31 / 100 + 3) : checked(BitLength(magnitude) + 2);
            var capacity = checked(Math.Max(naturalCapacity, precision > 0 ? precision : 0) + (negative ? 1 : 0));
            var buffer = new char[capacity];
            var index = buffer.Length;
            var digitCount = 0;
            if (radix == 10)
            {
                var work = Clone(magnitude);
                do
                {
                    var digit = DivideByUInt32(work, 10);
                    buffer[--index] = (char)('0' + digit);
                } while (EffectiveLength(work) != 0);
                digitCount = buffer.Length - index;
            }
            else
            {
                var bits = BitLength(magnitude);
                for (var bit = 0; bit < bits; bit += radix == 16 ? 4 : 1)
                {
                    var digit = 0;
                    var count = radix == 16 ? 4 : 1;
                    for (var offset = 0; offset < count && bit + offset < bits; offset++) if (GetMagnitudeBit(magnitude, bit + offset)) digit |= 1 << offset;
                    buffer[--index] = digit < 10 ? (char)('0' + digit) : (char)((upper ? 'A' : 'a') + digit - 10);
                }
                digitCount = buffer.Length - index;
            }
            if (precision > digitCount)
            {
                var padded = new char[precision + (negative ? 1 : 0)];
                var paddedIndex = 0;
                if (negative) padded[paddedIndex++] = '-';
                for (var zeroIndex = digitCount; zeroIndex < precision; zeroIndex++) padded[paddedIndex++] = '0';
                for (var digitIndex = index; digitIndex < buffer.Length; digitIndex++) padded[paddedIndex++] = buffer[digitIndex];
                return String.Create(padded);
            }
            if (negative) buffer[--index] = '-';
            return String.Create(buffer, index, buffer.Length - index);
        }

        private static BigInteger Bitwise(BigInteger left, BigInteger right, char operation)
        {
            var width = Math.Max(BitLength(left._bits ?? Array.Empty<uint>()), BitLength(right._bits ?? Array.Empty<uint>())) + 1;
            var a = left.ToTwosComplement(width);
            var b = right.ToTwosComplement(width);
            for (var index = 0; index < a.Length; index++) a[index] = operation switch { '&' => (uint)(a[index] & b[index]), '|' => (uint)(a[index] | b[index]), _ => (uint)(a[index] ^ b[index]) };
            return FromTwosComplement(a, width);
        }

        private uint[] ToTwosComplement(int width)
        {
            var words = new uint[(width + 31) / 32];
            if (_bits is not null) for (var index = 0; index < _bits.Length && index < words.Length; index++) words[index] = _bits[index];
            if (_sign < 0)
            {
                for (var index = 0; index < words.Length; index++) words[index] = ~words[index];
                var carry = 1u;
                for (var index = 0; index < words.Length && carry != 0; index++) { var sum = words[index] + carry; words[index] = sum; carry = sum < carry ? 1u : 0u; }
            }
            if ((width & 31) != 0) words[^1] &= (1u << (width & 31)) - 1;
            return words;
        }

        private static BigInteger FromTwosComplement(uint[] words, int width)
        {
            if ((width & 31) != 0) words[^1] &= (1u << (width & 31)) - 1;
            var negative = (words[^1] & (1u << ((width - 1) & 31))) != 0;
            if (!negative) return new BigInteger(1, words);
            for (var index = 0; index < words.Length; index++) words[index] = ~words[index];
            if ((width & 31) != 0) words[^1] &= (1u << (width & 31)) - 1;
            var carry = 1u;
            for (var index = 0; index < words.Length && carry != 0; index++) { var sum = words[index] + carry; words[index] = sum; carry = sum < carry ? 1u : 0u; }
            if ((width & 31) != 0) words[^1] &= (1u << (width & 31)) - 1;
            return new BigInteger(-1, words);
        }

        private static BigInteger FromInt64(long value)
        {
            if (value == 0) return default;
            var negative = value < 0;
            var magnitude = negative ? unchecked((ulong)(-(value + 1))) + 1UL : (ulong)value;
            return FromUnsigned(magnitude, negative ? -1 : 1);
        }

        private static BigInteger FromUInt64(ulong value) => FromUnsigned(value, 1);
        private static BigInteger FromUnsigned(ulong value, int sign)
        {
            if (value == 0) return default;
            return new BigInteger(sign, value <= uint.MaxValue ? new uint[] { (uint)value } : new uint[] { (uint)value, (uint)(value >> 32) });
        }

        private static BigInteger FromDecimal(decimal value)
        {
            var bits = decimal.GetBits(decimal.Truncate(value));
            var words = new uint[] { (uint)bits[0], (uint)bits[1], (uint)bits[2] };
            var magnitude = new BigInteger((bits[3] & unchecked((int)0x80000000)) == 0 ? 1 : -1, words);
            var scale = (bits[3] >> 16) & 0x7F;
            return scale == 0 ? magnitude : magnitude / Pow(10, scale);
        }

        private static BigInteger FromDouble(double value, bool throwOnOverflow)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { if (throwOnOverflow) throw new OverflowException(); return default; }
            var negative = value < 0;
            var bits = BitConverter.DoubleToUInt64Bits(negative ? -value : value);
            var exponent = (int)((bits >> 52) & 0x7FF) - 1023;
            var mantissa = (bits & 0x000F_FFFF_FFFF_FFFFUL) | 0x0010_0000_0000_0000UL;
            if (exponent < 0) return default;
            BigInteger result = exponent <= 52 ? FromUInt64(mantissa >> (52 - exponent)) : FromUInt64(mantissa) << (exponent - 52);
            return negative ? -result : result;
        }

        private static uint[]? BytesToMagnitude(byte[] bytes)
        {
            var length = (bytes.Length + 3) / 4;
            var bits = new uint[length];
            for (var index = 0; index < bytes.Length; index++) bits[index / 4] |= (uint)bytes[index] << ((index & 3) * 8);
            var last = bits.Length;
            while (last > 0 && bits[last - 1] == 0) last--;
            if (last == 0) return null;
            if (last != bits.Length)
            {
                var trimmed = new uint[last];
                for (var index = 0; index < last; index++) trimmed[index] = bits[index];
                return trimmed;
            }
            return bits;
        }

        private byte[] ToLittleEndianBytes(bool unsigned)
        {
            if (_sign == 0) return new byte[] { 0 };
            var byteCount = (_bits!.Length * 4);
            while (byteCount > 1 && (_bits[(byteCount - 1) / 4] >> (((byteCount - 1) & 3) * 8) & 0xFF) == 0) byteCount--;
            var bytes = new byte[byteCount];
            for (var index = 0; index < byteCount; index++) bytes[index] = (byte)(_bits[index / 4] >> ((index & 3) * 8));
            if (_sign > 0)
            {
                if (!unsigned && (bytes[^1] & 0x80) != 0)
                {
                    var expanded = new byte[bytes.Length + 1];
                    for (var index = 0; index < bytes.Length; index++) expanded[index] = bytes[index];
                    bytes = expanded;
                }
                return bytes;
            }
            var twos = new byte[bytes.Length];
            for (var index = 0; index < bytes.Length; index++) twos[index] = (byte)~bytes[index];
            var carry = 1;
            for (var index = 0; index < twos.Length; index++) { var sum = twos[index] + carry; twos[index] = (byte)sum; carry = sum >> 8; }
            if ((twos[^1] & 0x80) == 0)
            {
                var expanded = new byte[twos.Length + 1];
                for (var index = 0; index < twos.Length; index++) expanded[index] = twos[index];
                expanded[^1] = 0xFF;
                twos = expanded;
            }
            return twos;
        }

        private ulong ToUInt64() => _bits is null ? 0 : _bits.Length == 1 ? _bits[0] : ((ulong)_bits[1] << 32) | _bits[0];
        private double ToDouble()
        {
            var result = 0d;
            if (_bits is not null) for (var index = _bits.Length - 1; index >= 0; index--) result = result * 4294967296d + _bits[index];
            return _sign < 0 ? -result : result;
        }

        private static uint[] AddMagnitude(uint[] left, uint[] right)
        {
            var result = new uint[Math.Max(left.Length, right.Length) + 1];
            ulong carry = 0;
            for (var index = 0; index < result.Length - 1; index++) { var sum = carry + (index < left.Length ? left[index] : 0) + (index < right.Length ? right[index] : 0); result[index] = (uint)sum; carry = sum >> 32; }
            result[^1] = (uint)carry;
            return result;
        }

        private static uint[] SubtractMagnitude(uint[] left, uint[] right)
        {
            var result = new uint[left.Length];
            long borrow = 0;
            for (var index = 0; index < left.Length; index++) { var value = (long)left[index] - (index < right.Length ? right[index] : 0) - borrow; result[index] = (uint)value; borrow = value < 0 ? 1 : 0; }
            return result;
        }

        private static uint[] MultiplyMagnitude(uint[] left, uint[] right)
        {
            var result = new uint[left.Length + right.Length];
            for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
            {
                ulong carry = 0;
                for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
                {
                    var index = leftIndex + rightIndex;
                    var product = (ulong)left[leftIndex] * right[rightIndex] + result[index] + carry;
                    result[index] = (uint)product;
                    carry = product >> 32;
                }
                var carryIndex = leftIndex + right.Length;
                while (carry != 0) { var sum = result[carryIndex] + carry; result[carryIndex] = (uint)sum; carry = sum >> 32; carryIndex++; }
            }
            return result;
        }

        private static void DivideMagnitude(uint[] dividend, uint[] divisor, out uint[] quotient, out uint[] remainder)
        {
            var quotientWords = new uint[(BitLength(dividend) + 31) / 32];
            var rem = default(BigInteger);
            for (var bit = BitLength(dividend) - 1; bit >= 0; bit--)
            {
                var shifted = ShiftLeftMagnitude(rem._bits ?? Array.Empty<uint>(), 1);
                rem = new BigInteger(1, shifted);
                if (GetMagnitudeBit(dividend, bit)) rem += One;
                if (CompareMagnitude(rem._bits!, divisor) >= 0)
                {
                    rem = new BigInteger(1, SubtractMagnitude(rem._bits!, divisor));
                    quotientWords[bit / 32] |= 1u << (bit & 31);
                }
            }
            quotient = quotientWords;
            remainder = rem._bits ?? Array.Empty<uint>();
        }

        private static uint DivideByUInt32(uint[] value, uint divisor)
        {
            ulong remainder = 0;
            for (var index = value.Length - 1; index >= 0; index--) { var current = (remainder << 32) | value[index]; value[index] = (uint)(current / divisor); remainder = current % divisor; }
            return (uint)remainder;
        }

        private static int RotationWidth(BigInteger value)
        {
            var bitLength = BitLength(value._bits ?? Array.Empty<uint>());
            if (value._sign < 0 && !IsMagnitudePowerOfTwo(value._bits!)) bitLength++;
            return checked(Math.Max(32, (bitLength + 31) & ~31));
        }

        private static uint[] CopyWords(uint[] value, int width)
        {
            var words = new uint[width / 32];
            for (var index = 0; index < value.Length && index < words.Length; index++) words[index] = value[index];
            return words;
        }

        private static uint[] ShiftRightWords(uint[] value, int shift) => ShiftRightMagnitude(value, shift, out _);

        private static BigInteger Rotate(BigInteger value, long rotateAmount)
        {
            if (value.IsZero || rotateAmount == 0) return value;

            var width = RotationWidth(value);
            var words = value._sign < 0 ? value.ToTwosComplement(width) : CopyWords(value._bits!, width);
            var amount = (int)(rotateAmount % width);
            if (amount < 0) amount += width;
            if (amount != 0)
            {
                var rotated = new uint[words.Length];
                for (var sourceBit = 0; sourceBit < width; sourceBit++)
                {
                    if (!GetMagnitudeBit(words, sourceBit)) continue;
                    var destinationBit = (sourceBit + amount) % width;
                    rotated[destinationBit / 32] |= 1u << (destinationBit & 31);
                }
                words = rotated;
            }

            return value._sign < 0 ? FromTwosComplement(words, width) : new BigInteger(1, words);
        }

        private static uint[] ShiftLeftMagnitude(uint[] value, int shift)
        {
            if (value.Length == 0) return Array.Empty<uint>();
            var wordShift = shift / 32;
            var bitShift = shift & 31;
            var result = new uint[value.Length + wordShift + (bitShift == 0 ? 0 : 1)];
            ulong carry = 0;
            for (var index = 0; index < value.Length; index++) { var current = ((ulong)value[index] << bitShift) | carry; result[index + wordShift] = (uint)current; carry = current >> 32; }
            if (carry != 0) result[value.Length + wordShift] = (uint)carry;
            return result;
        }

        private static uint[] ShiftRightMagnitude(uint[] value, int shift, out bool discarded)
        {
            if (shift >= BitLength(value)) { discarded = value.Length != 0; return Array.Empty<uint>(); }
            var wordShift = shift / 32;
            var bitShift = shift & 31;
            var result = new uint[value.Length - wordShift];
            discarded = false;
            for (var index = 0; index < wordShift; index++) if (value[index] != 0) discarded = true;
            if (bitShift != 0 && (value[wordShift] & ((1u << bitShift) - 1)) != 0) discarded = true;
            ulong carry = 0;
            for (var index = value.Length - 1; index >= wordShift; index--) { var current = value[index]; result[index - wordShift] = (uint)((current >> bitShift) | carry); carry = bitShift == 0 ? 0 : (current & ((1u << bitShift) - 1)) << (32 - bitShift); }
            return result;
        }

        private static int CompareMagnitude(uint[] left, uint[] right)
        {
            var leftLength = EffectiveLength(left);
            var rightLength = EffectiveLength(right);
            if (leftLength != rightLength) return leftLength < rightLength ? -1 : 1;
            for (var index = leftLength - 1; index >= 0; index--) if (left[index] != right[index]) return left[index] < right[index] ? -1 : 1;
            return 0;
        }

        private static bool MagnitudesEqual(uint[] left, uint[] right) => CompareMagnitude(left, right) == 0;
        private static int EffectiveLength(uint[] value) { var length = value.Length; while (length > 0 && value[length - 1] == 0) length--; return length; }
        private static int BitLength(uint[] value) { var length = EffectiveLength(value); if (length == 0) return 0; return (length - 1) * 32 + (32 - BitOperations.LeadingZeroCount(value[length - 1])); }
        private static bool IsMagnitudePowerOfTwo(uint[] value) { var found = false; for (var index = 0; index < value.Length; index++) { if (value[index] == 0) continue; if (found || (value[index] & (value[index] - 1)) != 0) return false; found = true; } return found; }
        private static bool GetMagnitudeBit(uint[] value, int bit) => bit >= 0 && bit / 32 < value.Length && (value[bit / 32] & (1u << (bit & 31))) != 0;
        private static uint[] Clone(uint[] value) { var clone = new uint[value.Length]; for (var index = 0; index < value.Length; index++) clone[index] = value[index]; return clone; }
        private static bool IsWhite(char value) => value is ' ' or '\t' or '\r' or '\n';
        private static int Digit(char value) => value is >= '0' and <= '9' ? value - '0' : value is >= 'A' and <= 'F' ? value - 'A' + 10 : value is >= 'a' and <= 'f' ? value - 'a' + 10 : -1;

        private static bool TryGetPartialEnd(ReadOnlySpan<char> s, NumberStyles style, out int end)
        {
            end = 0;
            var hex = (style & NumberStyles.AllowHexSpecifier) != 0;
            var binary = (style & NumberStyles.AllowBinarySpecifier) != 0;
            var unsupported = style & ~(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign | NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier);
            if (hex && binary || unsupported != 0) return false;

            var index = 0;
            if ((style & NumberStyles.AllowLeadingWhite) != 0) while (index < s.Length && IsWhite(s[index])) index++;
            if (!hex && !binary && (style & NumberStyles.AllowLeadingSign) != 0 && index < s.Length && (s[index] == '+' || s[index] == '-')) index++;
            var firstDigit = index;
            var radix = binary ? 2u : hex ? 16u : 10u;
            while (index < s.Length)
            {
                var digit = Digit(s[index]);
                if (digit < 0 || (uint)digit >= radix) break;
                index++;
            }
            if (index == firstDigit) return false;
            if ((style & NumberStyles.AllowTrailingWhite) != 0) while (index < s.Length && IsWhite(s[index])) index++;
            end = index;
            return true;
        }

        private static bool TryConvertFrom<TOther>(TOther value, out BigInteger result) where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(BigInteger)) { result = (BigInteger)(object)value; return true; }
            if (typeof(TOther) == typeof(int)) { result = (int)(object)value; return true; }
            if (typeof(TOther) == typeof(uint)) { result = (uint)(object)value; return true; }
            if (typeof(TOther) == typeof(long)) { result = (long)(object)value; return true; }
            if (typeof(TOther) == typeof(ulong)) { result = (ulong)(object)value; return true; }
            if (typeof(TOther) == typeof(short)) { result = (short)(object)value; return true; }
            if (typeof(TOther) == typeof(ushort)) { result = (ushort)(object)value; return true; }
            if (typeof(TOther) == typeof(byte)) { result = (byte)(object)value; return true; }
            if (typeof(TOther) == typeof(sbyte)) { result = (sbyte)(object)value; return true; }
            if (typeof(TOther) == typeof(double)) { result = (BigInteger)(double)(object)value; return true; }
            if (typeof(TOther) == typeof(float)) { result = (BigInteger)(float)(object)value; return true; }
            if (typeof(TOther) == typeof(decimal)) { result = (BigInteger)(decimal)(object)value; return true; }
            result = default;
            return false;
        }

        private static bool TryConvertTo<TOther>(BigInteger value, out TOther result) where TOther : INumberBase<TOther>
        {
            object? converted = typeof(TOther) == typeof(BigInteger) ? value : typeof(TOther) == typeof(int) ? (int)value : typeof(TOther) == typeof(uint) ? (uint)value : typeof(TOther) == typeof(long) ? (long)value : typeof(TOther) == typeof(ulong) ? (ulong)value : typeof(TOther) == typeof(double) ? (double)value : typeof(TOther) == typeof(float) ? (float)value : null;
            if (converted is TOther typed) { result = typed; return true; }
            result = default!;
            return false;
        }
    }
}
