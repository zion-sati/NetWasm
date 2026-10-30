// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported verbatim from dotnet/runtime v11.0.0-rc.3.26522.148
// (474ed963699fc29809015392738a15231aaae2de), src/libraries/System.Private.CoreLib/src/System/Decimal.cs,
// up to IConvertible; generic math is in Decimal.GenericMath.cs. NetWasm differences are marked
// "NetWasm:".

using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Versioning;

namespace System
{
    // Implements the Decimal data type. The Decimal data type can
    // represent values ranging from -79,228,162,514,264,337,593,543,950,335 to
    // 79,228,162,514,264,337,593,543,950,335 with 28 significant digits. The
    // Decimal data type is ideally suited to financial calculations that
    // require a large number of significant digits and no round-off errors.
    //
    // The finite set of values of type Decimal are of the form m
    // / 10e, where m is an integer such that
    // -296 <; m <; 296, and e is an integer
    // between 0 and 28 inclusive.
    //
    // Contrary to the float and double data types, decimal
    // fractional numbers such as 0.1 can be represented exactly in the
    // Decimal representation. In the float and double
    // representations, such numbers are often infinite fractions, making those
    // representations more prone to round-off errors.
    //
    // The Decimal class implements widening conversions from the
    // ubyte, char, short, int, and long types
    // to Decimal. These widening conversions never lose any information
    // and never throw exceptions. The Decimal class also implements
    // narrowing conversions from Decimal to ubyte, char,
    // short, int, and long. These narrowing conversions round
    // the Decimal value towards zero to the nearest integer, and then
    // converts that integer to the destination type. An OverflowException
    // is thrown if the result is not within the range of the destination type.
    //
    // The Decimal class provides a widening conversion from
    // Currency to Decimal. This widening conversion never loses any
    // information and never throws exceptions. The Currency class provides
    // a narrowing conversion from Decimal to Currency. This
    // narrowing conversion rounds the Decimal to four decimals and then
    // converts that number to a Currency. An OverflowException
    // is thrown if the result is not within the range of the Currency type.
    //
    // The Decimal class provides narrowing conversions to and from the
    // float and double types. A conversion from Decimal to
    // float or double may lose precision, but will not lose
    // information about the overall magnitude of the numeric value, and will never
    // throw an exception. A conversion from float or double to
    // Decimal throws an OverflowException if the value is not within
    // the range of the Decimal type.
    [StructLayout(LayoutKind.Sequential)]
    [Serializable]
    // NetWasm: no [NonVersionable]; it only matters to ReadyToRun code versioning.
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public readonly partial struct Decimal
        : ISpanFormattable,
          IComparable,
          IConvertible,
          IComparable<decimal>,
          IEquatable<decimal>,
          // NetWasm: no ISerializable or IDeserializationCallback; there is no SerializationInfo.
          IFloatingPoint<decimal>,
          IMinMaxValue<decimal>,
          IUtf8SpanFormattable
    {
        // Sign mask for the flags field. A value of zero in this bit indicates a
        // positive Decimal value, and a value of one in this bit indicates a
        // negative Decimal value.
        //
        // Look at OleAut's DECIMAL_NEG constant to check for negative values
        // in native code.
        private const int SignMask = unchecked((int)0x80000000);

        // Scale mask for the flags field. This byte in the flags field contains
        // the power of 10 to divide the Decimal value by. The scale byte must
        // contain a value between 0 and 28 inclusive.
        private const int ScaleMask = 0x00FF0000;

        // Number of bits scale is shifted by.
        private const int ScaleShift = 16;

        // Constant representing the Decimal value 0.
        public const decimal Zero = 0m;

        // Constant representing the Decimal value 1.
        public const decimal One = 1m;

        // Constant representing the Decimal value -1.
        public const decimal MinusOne = -1m;

        // Constant representing the largest possible Decimal value. The value of
        // this constant is 79,228,162,514,264,337,593,543,950,335.
        public const decimal MaxValue = 79228162514264337593543950335m;

        // Constant representing the smallest possible Decimal value. The value of
        // this constant is -79,228,162,514,264,337,593,543,950,335.
        public const decimal MinValue = -79228162514264337593543950335m;

        /// <summary>Represents the additive identity (0).</summary>
        private const decimal AdditiveIdentity = 0m;

        /// <summary>Represents the multiplicative identity (1).</summary>
        private const decimal MultiplicativeIdentity = 1m;

        /// <summary>Represents the number negative one (-1).</summary>
        private const decimal NegativeOne = -1m;

        // The lo, mid, hi, and flags fields contain the representation of the
        // Decimal value. The lo, mid, and hi fields contain the 96-bit integer
        // part of the Decimal. Bits 0-15 (the lower word) of the flags field are
        // unused and must be zero; bits 16-23 contain must contain a value between
        // 0 and 28, indicating the power of 10 to divide the 96-bit integer part
        // by to produce the Decimal value; bits 24-30 are unused and must be zero;
        // and finally bit 31 indicates the sign of the Decimal value, 0 meaning
        // positive and 1 meaning negative.
        //
        // NOTE: Do not change the order and types of these fields. The layout has to
        // match Win32 DECIMAL type.
        private readonly int _flags;
        private readonly uint _hi32;
        private readonly ulong _lo64;

        // Constructs a Decimal from an integer value.
        //
        public Decimal(int value)
        {
            if (value >= 0)
            {
                _flags = 0;
            }
            else
            {
                _flags = SignMask;
                value = -value;
            }
            _lo64 = (uint)value;
            _hi32 = 0;
        }

        // Constructs a Decimal from an unsigned integer value.
        //
        [CLSCompliant(false)]
        public Decimal(uint value)
        {
            _flags = 0;
            _lo64 = value;
            _hi32 = 0;
        }

        // Constructs a Decimal from a long value.
        //
        public Decimal(long value)
        {
            if (value >= 0)
            {
                _flags = 0;
            }
            else
            {
                _flags = SignMask;
                value = -value;
            }
            _lo64 = (ulong)value;
            _hi32 = 0;
        }

        // Constructs a Decimal from an unsigned long value.
        //
        [CLSCompliant(false)]
        public Decimal(ulong value)
        {
            _flags = 0;
            _lo64 = value;
            _hi32 = 0;
        }

        // Constructs a Decimal from a float value.
        //
        public Decimal(float value)
        {
            DecCalc.VarDecFromR4(value, out AsMutable(ref this));
        }

        // Constructs a Decimal from a double value.
        //
        public Decimal(double value)
        {
            DecCalc.VarDecFromR8(value, out AsMutable(ref this));
        }

        // NetWasm: no serialization constructor or ISerializable.GetObjectData.

        //
        // Decimal <==> Currency conversion.
        //
        // A Currency represents a positive or negative decimal value with 4 digits past the decimal point. The actual Int64 representation used by these methods
        // is the currency value multiplied by 10,000. For example, a currency value of $12.99 would be represented by the Int64 value 129,900.
        //
        public static decimal FromOACurrency(long cy)
        {
            ulong absoluteCy; // has to be ulong to accommodate the case where cy == long.MinValue.
            bool isNegative = false;
            if (cy < 0)
            {
                isNegative = true;
                absoluteCy = (ulong)(-cy);
            }
            else
            {
                absoluteCy = (ulong)cy;
            }

            // In most cases, FromOACurrency() produces a Decimal with Scale set to 4. Unless, that is, some of the trailing digits past the decimal point are zero,
            // in which case, for compatibility with .Net, we reduce the Scale by the number of zeros. While the result is still numerically equivalent, the scale does
            // affect the ToString() value. In particular, it prevents a converted currency value of $12.95 from printing uglily as "12.9500".
            int scale = 4;
            if (absoluteCy != 0)  // For compatibility, a currency of 0 emits the Decimal "0.0000" (scale set to 4).
            {
                while (scale != 0 && ((absoluteCy % 10) == 0))
                {
                    scale--;
                    absoluteCy /= 10;
                }
            }

            return new decimal((int)absoluteCy, (int)(absoluteCy >> 32), 0, isNegative, (byte)scale);
        }

        public static long ToOACurrency(decimal value)
        {
            return DecCalc.VarCyFromDec(ref AsMutable(ref value));
        }

        private static bool IsValid(int flags) => (flags & ~(SignMask | ScaleMask)) == 0 && ((uint)(flags & ScaleMask) <= (28 << ScaleShift));

        // Constructs a Decimal from an integer array containing a binary
        // representation. The bits argument must be a non-null integer
        // array with four elements. bits[0], bits[1], and
        // bits[2] contain the low, middle, and high 32 bits of the 96-bit
        // integer part of the Decimal. bits[3] contains the scale factor
        // and sign of the Decimal: bits 0-15 (the lower word) are unused and must
        // be zero; bits 16-23 must contain a value between 0 and 28, indicating
        // the power of 10 to divide the 96-bit integer part by to produce the
        // Decimal value; bits 24-30 are unused and must be zero; and finally bit
        // 31 indicates the sign of the Decimal value, 0 meaning positive and 1
        // meaning negative.
        //
        // Note that there are several possible binary representations for the
        // same numeric value. For example, the value 1 can be represented as {1,
        // 0, 0, 0} (integer value 1 with a scale factor of 0) and equally well as
        // {1000, 0, 0, 0x30000} (integer value 1000 with a scale factor of 3).
        // The possible binary representations of a particular value are all
        // equally valid, and all are numerically equivalent.
        //
        public Decimal(int[] bits) :
            this((ReadOnlySpan<int>)(bits ?? throw new ArgumentNullException(nameof(bits))))
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="decimal"/> to a decimal value represented in binary and contained in the specified span.
        /// </summary>
        /// <param name="bits">A span of four <see cref="int"/>s containing a binary representation of a decimal value.</param>
        /// <exception cref="ArgumentException">The length of <paramref name="bits"/> is not 4, or the representation of the decimal value in <paramref name="bits"/> is not valid.</exception>
        public Decimal(ReadOnlySpan<int> bits)
        {
            if (bits.Length == 4)
            {
                int f = bits[3];
                if (IsValid(f))
                {
                    _lo64 = (uint)bits[0] + ((ulong)(uint)bits[1] << 32);
                    _hi32 = (uint)bits[2];
                    _flags = f;
                    return;
                }
            }
            throw new ArgumentException(SR.Arg_DecBitCtor);
        }

        // Constructs a Decimal from its constituent parts.
        //
        public Decimal(int lo, int mid, int hi, bool isNegative, byte scale)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 28);
            _lo64 = (uint)lo + ((ulong)(uint)mid << 32);
            _hi32 = (uint)hi;
            _flags = ((int)scale) << 16;
            if (isNegative)
                _flags |= SignMask;
        }

        // NetWasm: no IDeserializationCallback.OnDeserialization.

        // Constructs a Decimal from its constituent parts.
        private Decimal(int lo, int mid, int hi, int flags)
        {
            if (IsValid(flags))
            {
                _lo64 = (uint)lo + ((ulong)(uint)mid << 32);
                _hi32 = (uint)hi;
                _flags = flags;
                return;
            }
            throw new ArgumentException(SR.Arg_DecBitCtor);
        }

        private Decimal(in decimal d, int flags)
        {
            this = d;
            _flags = flags;
        }

        /// <summary>
        /// Gets the scaling factor of the decimal, which is a number from 0 to 28 that represents the number of decimal digits.
        /// </summary>
        public byte Scale => (byte)(_flags >> ScaleShift);

        private sbyte Exponent
        {
            get
            {
                // Decimal tracks its exponent as a scale between 0 and 28. This scale is used
                // with the significand as `significand / 10^scale`
                //
                // The IFloatingPoint contract however follows the general IEEE 754 algorithm
                // which is `-1^s * b^e * (b^(1-p) * m)`
                //
                // In this algorithm
                // * `s` is the sign
                // * `b` is the radix (10 for decimal)
                // * `e` is the exponent
                // * `p` is the number of bits in the significand
                // * `m` is the significand itself
                //
                // For a value such as decimal.MaxValue, the significand is 79228162514264337593543950335
                // and the scale is 0. Since decimal tracks 96 significand bits, the required algorithm (simplified)
                // gives us 7.9228162514264337593543950335 * 10^-67 * 10^e. To get back to our original value we
                // then need the exponent to be 95.
                //
                // For a value such as 1E-28, the significand is 1 and the scale is 28. The required algorithm (simplified)
                // gives us 1.0 * 10^-95 * 10^e. To get back to our original value we need the exponent to be 67.
                //
                // Given that scale is bound by 0 and 28, inclusive, the returned exponent will be between 95
                // and 67, inclusive. That is between `(p - 1)` and `(p - 1) - MaxScale`.
                //
                // The generalized algorithm for converting from scale to exponent is then `exponent = 95 - scale`.

                sbyte exponent = (sbyte)(95 - Scale);
                Debug.Assert((exponent >= 67) && (exponent <= 95));
                return exponent;
            }
        }

        // Adds two Decimal values.
        //
        public static decimal Add(decimal d1, decimal d2)
        {
            DecCalc.DecAddSub(ref AsMutable(ref d1), ref AsMutable(ref d2), false);
            return d1;
        }

        // Rounds a Decimal to an integer value. The Decimal argument is rounded
        // towards positive infinity.
        public static decimal Ceiling(decimal d)
        {
            int flags = d._flags;
            if ((flags & ScaleMask) != 0)
                DecCalc.InternalRound(ref AsMutable(ref d), (byte)(flags >> ScaleShift), MidpointRounding.ToPositiveInfinity);
            return d;
        }

        // Compares two Decimal values, returning an integer that indicates their
        // relationship.
        //
        public static int Compare(decimal d1, decimal d2)
        {
            return DecCalc.VarDecCmp(in d1, in d2);
        }

        // Compares this object to another object, returning an integer that
        // indicates the relationship.
        // Returns a value less than zero if this  object
        // null is considered to be less than any instance.
        // If object is not of type Decimal, this method throws an ArgumentException.
        //
        public int CompareTo(object? value)
        {
            if (value == null)
                return 1;
            if (!(value is decimal))
                throw new ArgumentException(SR.Arg_MustBeDecimal);

            decimal other = (decimal)value;
            return DecCalc.VarDecCmp(in this, in other);
        }

        public int CompareTo(decimal value)
        {
            return DecCalc.VarDecCmp(in this, in value);
        }

        // Divides two Decimal values.
        //
        public static decimal Divide(decimal d1, decimal d2)
        {
            DecCalc.VarDecDiv(ref AsMutable(ref d1), ref AsMutable(ref d2));
            return d1;
        }

        // Checks if this Decimal is equal to a given object. Returns true
        // if the given object is a boxed Decimal and its value is equal to the
        // value of this Decimal. Returns false otherwise.
        //
        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is decimal other &&
            DecCalc.Equals(in this, in other);

        public bool Equals(decimal value) =>
            DecCalc.Equals(in this, in value);

        // Returns the hash code for this Decimal.
        //
        public override int GetHashCode() => DecCalc.GetHashCode(in this);

        // Compares two Decimal values for equality. Returns true if the two
        // Decimal values are equal, or false if they are not equal.
        //
        public static bool Equals(decimal d1, decimal d2)
        {
            return DecCalc.Equals(in d1, in d2);
        }

        // Rounds a Decimal to an integer value. The Decimal argument is rounded
        // towards negative infinity.
        //
        public static decimal Floor(decimal d)
        {
            int flags = d._flags;
            if ((flags & ScaleMask) != 0)
                DecCalc.InternalRound(ref AsMutable(ref d), (byte)(flags >> ScaleShift), MidpointRounding.ToNegativeInfinity);
            return d;
        }

        // NetWasm: upstream's ToString, TryFormat, Parse and TryParse members go through
        // Number.FormatDecimal and Number.TryParseDecimal, which arrive with the numeric parsing and
        // formatting port. Until then ToString(), Parse(string) and TryParse(string?, out decimal) are
        // below and the other overloads are in Decimal.GenericMath.cs.

        // Returns a binary representation of a Decimal. The return value is an
        // integer array with four elements. Elements 0, 1, and 2 contain the low,
        // middle, and high 32 bits of the 96-bit integer part of the Decimal.
        // Element 3 contains the scale factor and sign of the Decimal: bits 0-15
        // (the lower word) are unused; bits 16-23 contain a value between 0 and
        // 28, indicating the power of 10 to divide the 96-bit integer part by to
        // produce the Decimal value; bits 24-30 are unused; and finally bit 31
        // indicates the sign of the Decimal value, 0 meaning positive and 1
        // meaning negative.
        //
        public static int[] GetBits(decimal d)
        {
            return [(int)d.Low, (int)d.Mid, (int)d.High, d._flags];
        }

        /// <summary>
        /// Converts the value of a specified instance of <see cref="decimal"/> to its equivalent binary representation.
        /// </summary>
        /// <param name="d">The value to convert.</param>
        /// <param name="destination">The span into which to store the four-integer binary representation.</param>
        /// <returns>Four, the number of integers in the binary representation.</returns>
        /// <exception cref="ArgumentException">The destination span was not long enough to store the binary representation.</exception>
        public static int GetBits(decimal d, Span<int> destination)
        {
            if (destination.Length <= 3)
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }

            destination[0] = (int)d.Low;
            destination[1] = (int)d.Mid;
            destination[2] = (int)d.High;
            destination[3] = d._flags;
            return 4;
        }

        /// <summary>
        /// Tries to convert the value of a specified instance of <see cref="decimal"/> to its equivalent binary representation.
        /// </summary>
        /// <param name="d">The value to convert.</param>
        /// <param name="destination">The span into which to store the binary representation.</param>
        /// <param name="valuesWritten">The number of integers written to the destination.</param>
        /// <returns>true if the decimal's binary representation was written to the destination; false if the destination wasn't long enough.</returns>
        public static bool TryGetBits(decimal d, Span<int> destination, out int valuesWritten)
        {
            if (destination.Length <= 3)
            {
                valuesWritten = 0;
                return false;
            }

            destination[0] = (int)d.Low;
            destination[1] = (int)d.Mid;
            destination[2] = (int)d.High;
            destination[3] = d._flags;
            valuesWritten = 4;
            return true;
        }

        internal static void GetBytes(in decimal d, Span<byte> buffer)
        {
            buffer = buffer.Slice(0, 16);

            BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)d.Low);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), (int)d.Mid);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), (int)d.High);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(12), d._flags);
        }

        internal static decimal ToDecimal(ReadOnlySpan<byte> span)
        {
            span = span.Slice(0, 16);

            int lo = BinaryPrimitives.ReadInt32LittleEndian(span);
            int mid = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4));
            int hi = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8));
            int flags = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12));
            return new decimal(lo, mid, hi, flags);
        }

        public static decimal Remainder(decimal d1, decimal d2)
        {
            DecCalc.VarDecMod(ref AsMutable(ref d1), ref AsMutable(ref d2));
            return d1;
        }

        // Multiplies two Decimal values.
        //
        public static decimal Multiply(decimal d1, decimal d2)
        {
            DecCalc.VarDecMul(ref AsMutable(ref d1), ref AsMutable(ref d2));
            return d1;
        }

        // Returns the negated value of the given Decimal. If d is non-zero,
        // the result is -d. If d is zero, the result is zero.
        //
        public static decimal Negate(decimal d)
        {
            return new decimal(in d, d._flags ^ SignMask);
        }

        // Rounds a Decimal value to a given number of decimal places. The value
        // given by d is rounded to the number of decimal places given by
        // decimals. The decimals argument must be an integer between
        // 0 and 28 inclusive.
        //
        // By default a mid-point value is rounded to the nearest even number. If the mode is
        // passed in, it can also round away from zero.

        public static decimal Round(decimal d) => Round(ref d, 0, MidpointRounding.ToEven);
        public static decimal Round(decimal d, int decimals) => Round(ref d, decimals, MidpointRounding.ToEven);
        public static decimal Round(decimal d, MidpointRounding mode) => Round(ref d, 0, mode);
        public static decimal Round(decimal d, int decimals, MidpointRounding mode) => Round(ref d, decimals, mode);

        private static decimal Round(ref decimal d, int decimals, MidpointRounding mode)
        {
            if ((uint)decimals > 28)
                throw new ArgumentOutOfRangeException(nameof(decimals), SR.ArgumentOutOfRange_DecimalRound);
            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity)
                // NetWasm: SR.Format's text built without composite formatting, which would link
                // every reachable IFormattable (DateTime's time zones included). An undefined
                // MidpointRounding formats as its number.
                throw new ArgumentException("The value '" + ((int)mode).ToString() + "' is not valid for this usage of the type MidpointRounding.", nameof(mode));

            int scale = d.Scale - decimals;
            if (scale > 0)
                DecCalc.InternalRound(ref AsMutable(ref d), (uint)scale, mode);
            return d;
        }

        // Subtracts two Decimal values.
        //
        public static decimal Subtract(decimal d1, decimal d2)
        {
            DecCalc.DecAddSub(ref AsMutable(ref d1), ref AsMutable(ref d2), true);
            return d1;
        }

        // Converts a Decimal to an unsigned byte. The Decimal value is rounded
        // towards zero to the nearest integer value, and the result of this
        // operation is returned as a byte.
        //
        public static byte ToByte(decimal value)
        {
            uint temp;
            try
            {
                temp = ToUInt32(value);
            }
            catch (OverflowException)
            {
                Number.ThrowOverflowException<byte>();
                throw;
            }
            if (temp != (byte)temp) Number.ThrowOverflowException<byte>();
            return (byte)temp;
        }

        // Converts a Decimal to a signed byte. The Decimal value is rounded
        // towards zero to the nearest integer value, and the result of this
        // operation is returned as a byte.
        //
        [CLSCompliant(false)]
        public static sbyte ToSByte(decimal value)
        {
            int temp;
            try
            {
                temp = ToInt32(value);
            }
            catch (OverflowException)
            {
                Number.ThrowOverflowException<sbyte>();
                throw;
            }
            if (temp != (sbyte)temp) Number.ThrowOverflowException<sbyte>();
            return (sbyte)temp;
        }

        // Converts a Decimal to a short. The Decimal value is
        // rounded towards zero to the nearest integer value, and the result of
        // this operation is returned as a short.
        //
        public static short ToInt16(decimal value)
        {
            int temp;
            try
            {
                temp = ToInt32(value);
            }
            catch (OverflowException)
            {
                Number.ThrowOverflowException<short>();
                throw;
            }
            if (temp != (short)temp) Number.ThrowOverflowException<short>();
            return (short)temp;
        }

        // Converts a Decimal to a double. Since a double has fewer significant
        // digits than a Decimal, this operation may produce round-off errors.
        //
        public static double ToDouble(decimal d)
        {
            return DecimalMath.ToDouble(d); // NetWasm: DecCalc.VarR8FromDec needs Number.ComputeFloat.
        }

        // Converts a Decimal to an integer. The Decimal value is rounded towards
        // zero to the nearest integer value, and the result of this operation is
        // returned as an integer.
        //
        public static int ToInt32(decimal d)
        {
            Truncate(ref d);
            if ((d.High | d.Mid) == 0)
            {
                int i = (int)d.Low;
                if (!IsNegative(d))
                {
                    if (i >= 0) return i;
                }
                else
                {
                    i = -i;
                    if (i <= 0) return i;
                }
            }
            throw new OverflowException(SR.Overflow_Int32);
        }

        // Converts a Decimal to a long. The Decimal value is rounded towards zero
        // to the nearest integer value, and the result of this operation is
        // returned as a long.
        //
        public static long ToInt64(decimal d)
        {
            Truncate(ref d);
            if (d.High == 0)
            {
                long l = (long)d.Low64;
                if (!IsNegative(d))
                {
                    if (l >= 0) return l;
                }
                else
                {
                    l = -l;
                    if (l <= 0) return l;
                }
            }
            throw new OverflowException(SR.Overflow_Int64);
        }

        // Converts a Decimal to an ushort. The Decimal
        // value is rounded towards zero to the nearest integer value, and the
        // result of this operation is returned as an ushort.
        //
        [CLSCompliant(false)]
        public static ushort ToUInt16(decimal value)
        {
            uint temp;
            try
            {
                temp = ToUInt32(value);
            }
            catch (OverflowException)
            {
                Number.ThrowOverflowException<ushort>();
                throw;
            }
            if (temp != (ushort)temp) Number.ThrowOverflowException<ushort>();
            return (ushort)temp;
        }

        // Converts a Decimal to an unsigned integer. The Decimal
        // value is rounded towards zero to the nearest integer value, and the
        // result of this operation is returned as an unsigned integer.
        //
        [CLSCompliant(false)]
        public static uint ToUInt32(decimal d)
        {
            Truncate(ref d);
            if ((d.High | d.Mid) == 0)
            {
                uint i = d.Low;
                if (!IsNegative(d) || i == 0)
                    return i;
            }
            throw new OverflowException(SR.Overflow_UInt32);
        }

        // Converts a Decimal to an unsigned long. The Decimal
        // value is rounded towards zero to the nearest integer value, and the
        // result of this operation is returned as a long.
        //
        [CLSCompliant(false)]
        public static ulong ToUInt64(decimal d)
        {
            Truncate(ref d);
            if (d.High == 0)
            {
                ulong l = d.Low64;
                if (!IsNegative(d) || l == 0)
                    return l;
            }
            throw new OverflowException(SR.Overflow_UInt64);
        }

        // Converts a Decimal to a float. Since a float has fewer significant
        // digits than a Decimal, this operation may produce round-off errors.
        //
        public static float ToSingle(decimal d)
        {
            return DecimalMath.ToSingle(d); // NetWasm: DecCalc.VarR4FromDec needs Number.ComputeFloat.
        }

        // Truncates a Decimal to an integer value. The Decimal argument is rounded
        // towards zero to the nearest integer value, corresponding to removing all
        // digits after the decimal point.
        //
        public static decimal Truncate(decimal d)
        {
            Truncate(ref d);
            return d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Truncate(ref decimal d)
        {
            int flags = d._flags;
            if ((flags & ScaleMask) != 0)
                DecCalc.InternalRound(ref AsMutable(ref d), (byte)(flags >> ScaleShift), MidpointRounding.ToZero);
        }

        public static implicit operator decimal(byte value) => new decimal((uint)value);

        [CLSCompliant(false)]
        public static implicit operator decimal(sbyte value) => new decimal(value);

        public static implicit operator decimal(short value) => new decimal(value);

        [CLSCompliant(false)]
        public static implicit operator decimal(ushort value) => new decimal((uint)value);

        public static implicit operator decimal(char value) => new decimal((uint)value);

        public static implicit operator decimal(int value) => new decimal(value);

        [CLSCompliant(false)]
        public static implicit operator decimal(uint value) => new decimal(value);

        public static implicit operator decimal(long value) => new decimal(value);

        [CLSCompliant(false)]
        public static implicit operator decimal(ulong value) => new decimal(value);

        public static explicit operator decimal(float value) => new decimal(value);

        public static explicit operator decimal(double value) => new decimal(value);

        public static explicit operator byte(decimal value) => ToByte(value);

        [CLSCompliant(false)]
        public static explicit operator sbyte(decimal value) => ToSByte(value);

        public static explicit operator char(decimal value)
        {
            ushort temp;
            try
            {
                temp = ToUInt16(value);
            }
            catch (OverflowException e)
            {
                throw new OverflowException(SR.Overflow_Char, e);
            }
            return (char)temp;
        }

        public static explicit operator short(decimal value) => ToInt16(value);

        [CLSCompliant(false)]
        public static explicit operator ushort(decimal value) => ToUInt16(value);

        public static explicit operator int(decimal value) => ToInt32(value);

        [CLSCompliant(false)]
        public static explicit operator uint(decimal value) => ToUInt32(value);

        public static explicit operator long(decimal value) => ToInt64(value);

        [CLSCompliant(false)]
        public static explicit operator ulong(decimal value) => ToUInt64(value);

        public static explicit operator float(decimal value) => DecimalMath.ToSingle(value); // NetWasm: as ToSingle.

        public static explicit operator double(decimal value) => DecimalMath.ToDouble(value); // NetWasm: as ToDouble.

        public static decimal operator +(decimal d) => d;

        public static decimal operator -(decimal d) => new decimal(in d, d._flags ^ SignMask);

        /// <inheritdoc cref="IIncrementOperators{TSelf}.op_Increment(TSelf)" />
        public static decimal operator ++(decimal d) => Add(d, One);

        /// <inheritdoc cref="IDecrementOperators{TSelf}.op_Decrement(TSelf)" />
        public static decimal operator --(decimal d) => Subtract(d, One);

        public static decimal operator +(decimal d1, decimal d2)
        {
            DecCalc.DecAddSub(ref AsMutable(ref d1), ref AsMutable(ref d2), false);
            return d1;
        }

        public static decimal operator -(decimal d1, decimal d2)
        {
            DecCalc.DecAddSub(ref AsMutable(ref d1), ref AsMutable(ref d2), true);
            return d1;
        }

        /// <inheritdoc cref="IMultiplyOperators{TSelf, TOther, TResult}.op_Multiply(TSelf, TOther)" />
        public static decimal operator *(decimal d1, decimal d2)
        {
            DecCalc.VarDecMul(ref AsMutable(ref d1), ref AsMutable(ref d2));
            return d1;
        }

        /// <inheritdoc cref="IDivisionOperators{TSelf, TOther, TResult}.op_Division(TSelf, TOther)" />
        public static decimal operator /(decimal d1, decimal d2)
        {
            DecCalc.VarDecDiv(ref AsMutable(ref d1), ref AsMutable(ref d2));
            return d1;
        }

        /// <inheritdoc cref="IModulusOperators{TSelf, TOther, TResult}.op_Modulus(TSelf, TOther)" />
        public static decimal operator %(decimal d1, decimal d2)
        {
            DecCalc.VarDecMod(ref AsMutable(ref d1), ref AsMutable(ref d2));
            return d1;
        }

        /// <inheritdoc cref="IEqualityOperators{TSelf, TOther, TResult}.op_Equality(TSelf, TOther)" />
        public static bool operator ==(decimal d1, decimal d2) => DecCalc.Equals(in d1, in d2);

        /// <inheritdoc cref="IEqualityOperators{TSelf, TOther, TResult}.op_Inequality(TSelf, TOther)" />
        public static bool operator !=(decimal d1, decimal d2) => !DecCalc.Equals(in d1, in d2);

        /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_LessThan(TSelf, TOther)" />
        public static bool operator <(decimal d1, decimal d2) => DecCalc.VarDecCmp(in d1, in d2) < 0;

        /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_LessThanOrEqual(TSelf, TOther)" />
        public static bool operator <=(decimal d1, decimal d2) => DecCalc.VarDecCmp(in d1, in d2) <= 0;

        /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_GreaterThan(TSelf, TOther)" />
        public static bool operator >(decimal d1, decimal d2) => DecCalc.VarDecCmp(in d1, in d2) > 0;

        /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_GreaterThanOrEqual(TSelf, TOther)" />
        public static bool operator >=(decimal d1, decimal d2) => DecCalc.VarDecCmp(in d1, in d2) >= 0;

        //
        // IConvertible implementation
        //

        public TypeCode GetTypeCode()
        {
            return TypeCode.Decimal;
        }

        bool IConvertible.ToBoolean(IFormatProvider? provider)
        {
            return Convert.ToBoolean(this);
        }

        char IConvertible.ToChar(IFormatProvider? provider)
        {
            throw new InvalidCastException("Invalid cast from 'Decimal' to 'Char'."); // NetWasm: SR.Format's text, as above.
        }

        sbyte IConvertible.ToSByte(IFormatProvider? provider)
        {
            return Convert.ToSByte(this);
        }

        byte IConvertible.ToByte(IFormatProvider? provider)
        {
            return Convert.ToByte(this);
        }

        short IConvertible.ToInt16(IFormatProvider? provider)
        {
            return Convert.ToInt16(this);
        }

        ushort IConvertible.ToUInt16(IFormatProvider? provider)
        {
            return Convert.ToUInt16(this);
        }

        int IConvertible.ToInt32(IFormatProvider? provider)
        {
            return Convert.ToInt32(this);
        }

        uint IConvertible.ToUInt32(IFormatProvider? provider)
        {
            return Convert.ToUInt32(this);
        }

        long IConvertible.ToInt64(IFormatProvider? provider)
        {
            return Convert.ToInt64(this);
        }

        ulong IConvertible.ToUInt64(IFormatProvider? provider)
        {
            return Convert.ToUInt64(this);
        }

        float IConvertible.ToSingle(IFormatProvider? provider)
        {
            return DecimalMath.ToSingle(this); // NetWasm: as ToSingle.
        }

        double IConvertible.ToDouble(IFormatProvider? provider)
        {
            return DecimalMath.ToDouble(this); // NetWasm: as ToDouble.
        }

        decimal IConvertible.ToDecimal(IFormatProvider? provider)
        {
            return this;
        }

        DateTime IConvertible.ToDateTime(IFormatProvider? provider)
        {
            throw new InvalidCastException("Invalid cast from 'Decimal' to 'DateTime'."); // NetWasm: SR.Format's text, as above.
        }

        object IConvertible.ToType(Type type, IFormatProvider? provider)
        {
            return Convert.DefaultToType(this, type, provider); // NetWasm: decimal's own members, not IConvertible's.
        }

        /// <inheritdoc cref="INumberBase{TSelf}.Abs(TSelf)" />
        public static decimal Abs(decimal value)
        {
            return new decimal(in value, value._flags & ~SignMask);
        }

        /// <inheritdoc cref="INumber{TSelf}.Clamp(TSelf, TSelf, TSelf)" />
        public static decimal Clamp(decimal value, decimal min, decimal max) => Math.Clamp(value, min, max);

        /// <inheritdoc cref="INumber{TSelf}.Max(TSelf, TSelf)" />
        public static decimal Max(decimal x, decimal y)
        {
            return DecCalc.VarDecCmp(in x, in y) >= 0 ? x : y;
        }

        /// <inheritdoc cref="INumber{TSelf}.Min(TSelf, TSelf)" />
        public static decimal Min(decimal x, decimal y)
        {
            return DecCalc.VarDecCmp(in x, in y) < 0 ? x : y;
        }

        /// <inheritdoc cref="INumber{TSelf}.Sign(TSelf)" />
        public static int Sign(decimal d) => (d.Low64 | d.High) == 0 ? 0 : (d._flags >> 31) | 1;

        /// <inheritdoc cref="INumberBase{TSelf}.IsNegative(TSelf)" />
        public static bool IsNegative(decimal value) => value._flags < 0;

        // NetWasm: interim members until the numeric parsing and formatting port, which also
        // brings Number.ComputeFloat for decimal to float and double. Parse(string) and
        // TryParse(string?, out decimal) are upstream's; the rest is not from upstream.

        private const int MaximumScale = 28;

        private Decimal(uint low, uint middle, uint high, bool negative, int scale)
        {
            _lo64 = low | ((ulong)middle << 32);
            _hi32 = high;
            _flags = (scale << ScaleShift) | (negative ? SignMask : 0);
        }

        public static decimal Parse(string s) => Parse(s, NumberStyles.Number, provider: null);

        public static bool TryParse([NotNullWhen(true)] string? s, out decimal result) => TryParse(s, NumberStyles.Number, provider: null, out result);

        public override string ToString() => DecimalMath.Format(this);

        private bool IsNegativeValue => _flags < 0;

        private bool IsZero => (_lo64 | _hi32) == 0;

        private static class DecimalMath
        {

            internal static decimal Round(decimal value, int decimals, MidpointRounding mode)
            {
                if (value.Scale <= decimals)
                {
                    return value;
                }
                DecCalc.InternalRound(
                    ref AsMutable(ref value),
                    (uint)(value.Scale - decimals),
                    mode);
                return value;
            }

            internal static double ToDouble(decimal value)
            {
                var result = value.High == 0 &&
                    value.Low64 <= 2UL << 52 &&
                    value.Scale <= 22
                        ? (double)value.Low64 / PowerOfTenDouble(value.Scale)
                        : DecimalToFloatingPointExact(
                            value.Low64, value.High, value.Scale, 53);
                return value.IsNegativeValue ? -result : result;
            }

            internal static float ToSingle(decimal value)
            {
                var result = value.High == 0 &&
                    value.Low64 <= 2UL << 23 &&
                    value.Scale <= 10
                        ? (float)((double)value.Low64 / PowerOfTenDouble(value.Scale))
                        : (float)DecimalToFloatingPointExact(
                            value.Low64, value.High, value.Scale, 24);
                return value.IsNegativeValue ? -result : result;
            }

            private static double PowerOfTenDouble(int exponent) => Pow10DoubleTable[exponent];

            private static ReadOnlySpan<double> Pow10DoubleTable =>
            [
                1d, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7,
                1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
                1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
            ];

            private static double DecimalToFloatingPointExact(ulong low64, uint high, int scale, int significandBits)
            {
                var mantissa = new UInt128(high, low64);
                if (mantissa == UInt128.Zero)
                {
                    return 0.0;
                }
                var divisor = PowerOfFive(scale);
                var mantissaBits = 128 - (int)UInt128.LeadingZeroCount(mantissa);
                var divisorBits = 128 - (int)UInt128.LeadingZeroCount(divisor);

                // Port of Decimal.DecCalc's .NET 11 exact path. Divide by 5^scale
                // with one guard bit and at most one extra bit. Both shifted
                // operands fit UInt128 for the full 96-bit/28-scale domain.
                var shift = (significandBits + 1) - (mantissaBits - divisorBits);
                UInt128 numerator, denominator;
                if (shift >= 0)
                {
                    numerator = mantissa << shift;
                    denominator = divisor;
                }
                else
                {
                    numerator = mantissa;
                    denominator = divisor << -shift;
                }
                var (quotient, remainder) = UInt128.DivRem(numerator, denominator);
                var quotientBits = 128 - (int)UInt128.LeadingZeroCount(quotient);
                var drop = quotientBits - significandBits;
                var keep = (ulong)(quotient >> drop);
                var roundBits = (ulong)(quotient & ((UInt128.One << drop) - 1));
                var half = 1UL << (drop - 1);
                var sticky = remainder != UInt128.Zero || (roundBits & (half - 1)) != 0;

                bool roundUp;
                if (roundBits > half)
                {
                    roundUp = true;
                }
                else if (roundBits < half)
                {
                    roundUp = false;
                }
                else
                {
                    roundUp = sticky || (keep & 1) != 0;
                }
                if (roundUp && ++keep == (1UL << significandBits))
                {
                    keep >>= 1;
                    drop++;
                }
                // Decimal's range is normal and finite in both formats. Scaling
                // the already-rounded significand by this power of two is exact.
                return Math.ScaleB((double)keep, drop - shift - scale);
            }

            private static UInt128 PowerOfFive(int exponent)
            {
                ReadOnlySpan<ulong> powers =
                [
                    1, 5, 25, 125, 625, 3125, 15625, 78125, 390625, 1953125,
                    9765625, 48828125, 244140625, 1220703125, 6103515625, 30517578125,
                    152587890625, 762939453125, 3814697265625, 19073486328125,
                    95367431640625, 476837158203125, 2384185791015625, 11920928955078125,
                    59604644775390625, 298023223876953125, 1490116119384765625,
                    7450580596923828125, 359414837200037393
                ];
                return new UInt128(exponent == MaximumScale ? 2U : 0U, powers[exponent]);
            }

            internal static bool TryParse(
                string? text,
                out decimal result,
                out bool overflow)
            {
                if (text == null)
                {
                    result = Zero;
                    overflow = false;
                    return false;
                }
                return TryParse(text.AsSpan(), out result, out overflow);
            }

            internal static bool TryParse(
                ReadOnlySpan<char> text,
                out decimal result,
                out bool overflow)
            {
                result = Zero;
                overflow = false;
                if (text.Length == 0)
                {
                    return false;
                }
                var start = 0;
                var end = text.Length;
                for (var scan = 0; scan < text.Length; scan++)
                {
                    if (scan == start && text[scan] == ' ')
                    {
                        start++;
                    }
                }
                for (var scan = text.Length - 1; scan >= 0; scan--)
                {
                    if (scan == end - 1 && text[scan] == ' ')
                    {
                        end--;
                    }
                }
                if (start == end)
                {
                    return false;
                }
                var index = start;
                var negative = false;
                if (text[index] == '+' || text[index] == '-')
                {
                    negative = text[index++] == '-';
                    if (index == end)
                    {
                        return false;
                    }
                }
                var coefficient = ZeroValue();
                var digits = 0;
                var fractionalDigits = 0;
                var decimalPoint = false;
                var mantissa = true;
                var coefficientValid = true;
                for (var scan = index; scan < end; scan++)
                {
                    var character = text[scan];
                    if (mantissa && character >= '0' && character <= '9')
                    {
                        if (coefficientValid)
                        {
                            coefficientValid = TryAppendDigit(
                                ref coefficient,
                                (uint)(character - '0'));
                        }
                        digits++;
                        if (decimalPoint)
                        {
                            fractionalDigits++;
                        }
                        index = scan + 1;
                    }
                    else if (mantissa && character == '.' && !decimalPoint)
                    {
                        decimalPoint = true;
                        index = scan + 1;
                    }
                    else
                    {
                        mantissa = false;
                    }
                }
                if (digits == 0)
                {
                    return false;
                }
                if (!coefficientValid)
                {
                    overflow = true;
                    return false;
                }
                var exponent = 0;
                if (index < end && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                    var exponentNegative = false;
                    if (index < end && (text[index] == '+' || text[index] == '-'))
                    {
                        exponentNegative = text[index++] == '-';
                    }
                    if (index == end)
                    {
                        return false;
                    }
                    var exponentDigits = 0;
                    var exponentActive = true;
                    for (var scan = index; scan < end; scan++)
                    {
                        var character = text[scan];
                        if (exponentActive && character >= '0' && character <= '9')
                        {
                            if (exponent < 1_000)
                            {
                                exponent = exponent * 10 + character - '0';
                            }
                            exponentDigits++;
                            index = scan + 1;
                        }
                        else
                        {
                            exponentActive = false;
                        }
                    }
                    if (exponentDigits == 0)
                    {
                        return false;
                    }
                    if (exponentNegative)
                    {
                        exponent = -exponent;
                    }
                }
                if (index != end)
                {
                    return false;
                }
                var scale = fractionalDigits - exponent;
                if (scale < 0)
                {
                    MultiplyPower10(ref coefficient, -scale);
                    scale = 0;
                }
                try
                {
                    result = Create(coefficient, scale, negative);
                    return true;
                }
                catch (OverflowException)
                {
                    overflow = true;
                    return false;
                }
            }

            internal struct WideInteger
            {
                internal uint Part0;
                internal uint Part1;
                internal uint Part2;
                internal uint Part3;
                internal uint Part4;
                internal uint Part5;

                internal int Length => 6;

                internal uint this[int index]
                {
                    get
                    {
                        if (index == 0) return Part0;
                        if (index == 1) return Part1;
                        if (index == 2) return Part2;
                        if (index == 3) return Part3;
                        if (index == 4) return Part4;
                        if (index == 5) return Part5;
                        return 0;
                    }
                    set
                    {
                        if (index == 0) Part0 = value;
                        else if (index == 1) Part1 = value;
                        else if (index == 2) Part2 = value;
                        else if (index == 3) Part3 = value;
                        else if (index == 4) Part4 = value;
                        else if (index == 5) Part5 = value;
                    }
                }
            }

            private static bool TryAppendDigit(ref WideInteger coefficient, uint digit)
            {
                try
                {
                    MultiplyWordInPlace(ref coefficient, 10);
                    AddWordInPlace(ref coefficient, digit);
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            internal static string Format(decimal value)
            {
                var builder = new Text.ValueStringBuilder(32);
                AppendFormat(ref builder, value);
                return builder.ToString();
            }

            internal static void AppendFormat(scoped ref Text.ValueStringBuilder builder, decimal value)
            {
                var coefficient = Coefficient(value);
                var digits = ToDigits(coefficient);
                var scale = value.Scale;
                var integerDigits = digits.Length - scale;
                if (value.IsNegativeValue)
                {
                    builder.Append('-');
                }
                if (scale == 0)
                {
                    builder.Append(digits);
                    return;
                }
                if (integerDigits <= 0)
                {
                    builder.Append('0');
                    builder.Append('.');
                    for (var zero = integerDigits; zero < 0; zero++)
                    {
                        builder.Append('0');
                    }
                    builder.Append(digits);
                    return;
                }
                for (var index = 0; index < digits.Length; index++)
                {
                    if (index == integerDigits)
                    {
                        builder.Append('.');
                    }
                    builder.Append(digits[index]);
                }
            }

            private static decimal Create(WideInteger coefficient, int scale, bool negative)
            {
                if (scale < 0)
                {
                    MultiplyPower10(ref coefficient, -scale);
                    scale = 0;
                }
                if (scale <= MaximumScale && Fits96(coefficient))
                {
                    return CreateUnchecked(coefficient, scale, negative);
                }
                if (scale == 0)
                {
                    throw new OverflowException();
                }
                var minimumRemoval = scale > MaximumScale ? scale - MaximumScale : 0;
                return CreateAtRemoval(coefficient, scale, negative, minimumRemoval);
            }

            private static decimal CreateAtRemoval(
                WideInteger coefficient,
                int scale,
                bool negative,
                int removal)
            {
                if (removal > scale)
                {
                    throw new OverflowException();
                }
                var divisor = Power10(removal);
                var division = DivRem(coefficient, divisor);
                RoundQuotient(ref division.Quotient, division.Remainder, divisor, negative,
                    MidpointRounding.ToEven);
                return Fits96(division.Quotient)
                    ? CreateUnchecked(division.Quotient, scale - removal, negative)
                    : CreateAtRemoval(coefficient, scale, negative, removal + 1);
            }

            private static decimal CreateUnchecked(WideInteger coefficient, int scale, bool negative) =>
                new(
                    coefficient[0],
                    coefficient.Length > 1 ? coefficient[1] : 0,
                    coefficient.Length > 2 ? coefficient[2] : 0,
                    negative,
                    scale);

            internal static WideInteger Coefficient(decimal value)
            {
                var low = value.Low;
                var middle = value.Mid;
                var high = value.High;
                var result = new WideInteger();
                result[0] = low;
                result[1] = middle;
                result[2] = high;
                return result;
            }

            private static void RoundQuotient(
                ref WideInteger quotient,
                WideInteger remainder,
                WideInteger divisor,
                bool negative,
                MidpointRounding mode)
            {
                if (IsZero(remainder))
                {
                    return;
                }
                var increment = mode switch
                {
                    MidpointRounding.ToZero => false,
                    MidpointRounding.ToNegativeInfinity => negative,
                    MidpointRounding.ToPositiveInfinity => !negative,
                    MidpointRounding.AwayFromZero => Compare(MultiplyWord(remainder, 2), divisor) >= 0,
                    _ => Compare(MultiplyWord(remainder, 2), divisor) > 0 ||
                         Compare(MultiplyWord(remainder, 2), divisor) == 0 &&
                         (quotient[0] & 1) != 0,
                };
                if (increment)
                {
                    AddWordInPlace(ref quotient, 1);
                }
            }

            internal static char[] ToDigits(WideInteger value)
            {
                if (IsZero(value))
                {
                    return ['0'];
                }
                var work = Copy(value);
                var reversed = new char[work.Length * 10];
                var length = 0;
                while (!IsZero(work))
                {
                    work = DivideWord(work, 10, out var remainder);
                    reversed[length++] = (char)('0' + remainder);
                }
                var result = new char[length];
                for (var index = 0; index < length; index++)
                {
                    result[index] = reversed[length - index - 1];
                }
                return result;
            }

            private static WideInteger Power10(int power)
            {
                var result = CopyWithCapacity(FromUInt64(1), 6);
                MultiplyPower10(ref result, power);
                return result;
            }

            private static void MultiplyPower10(ref WideInteger value, int power)
            {
                if (!TryMultiplyPower10(ref value, power))
                {
                    throw new OverflowException();
                }
            }

            private static bool TryMultiplyPower10(ref WideInteger value, int power)
            {
                var result = value;
                while (power >= 9)
                {
                    if (!TryMultiplyWordInPlace(ref result, 1_000_000_000U))
                    {
                        return false;
                    }
                    power -= 9;
                }
                if (power != 0 &&
                    !TryMultiplyWordInPlace(ref result, Power10Word(power)))
                {
                    return false;
                }
                value = result;
                return true;
            }

            private static uint Power10Word(int power)
            {
                if (power == 1) return 10U;
                if (power == 2) return 100U;
                if (power == 3) return 1_000U;
                if (power == 4) return 10_000U;
                if (power == 5) return 100_000U;
                if (power == 6) return 1_000_000U;
                if (power == 7) return 10_000_000U;
                if (power == 8) return 100_000_000U;
                if (power == 9) return 1_000_000_000U;
                return 1U;
            }

            private static WideInteger MultiplyWord(WideInteger value, uint multiplier)
            {
                var result = CopyWithCapacity(value, SignificantLength(value) + 1);
                MultiplyWordInPlace(ref result, multiplier);
                return Trim(result);
            }

            private static void MultiplyWordInPlace(ref WideInteger value, uint multiplier)
            {
                if (!TryMultiplyWordInPlace(ref value, multiplier))
                {
                    throw new OverflowException();
                }
            }

            private static bool TryMultiplyWordInPlace(
                ref WideInteger value,
                uint multiplier)
            {
                var carry = 0UL;
                for (var index = 0; index < value.Length; index++)
                {
                    var product = (ulong)value[index] * multiplier + carry;
                    value[index] = (uint)product;
                    carry = product >> 32;
                }
                if (carry != 0)
                {
                    return false;
                }
                return true;
            }

            private static void AddWordInPlace(ref WideInteger value, uint addition)
            {
                var carry = (ulong)addition;
                for (var index = 0; index < value.Length; index++)
                {
                    if (carry != 0)
                    {
                        var sum = value[index] + carry;
                        value[index] = (uint)sum;
                        carry = sum >> 32;
                    }
                }
                if (carry != 0)
                {
                    throw new OverflowException();
                }
            }

            internal static WideInteger DivideWord(WideInteger value, uint divisor, out uint remainder)
            {
                var result = new WideInteger();
                var carry = 0UL;
                for (var index = value.Length - 1; index >= 0; index--)
                {
                    var dividend = (carry << 32) | value[index];
                    result[index] = (uint)(dividend / divisor);
                    carry = dividend % divisor;
                }
                remainder = (uint)carry;
                return Trim(result);
            }

            private struct DivisionResult(WideInteger quotient, WideInteger remainder)
            {
                internal WideInteger Quotient = quotient;
                internal WideInteger Remainder = remainder;
            }

            private static DivisionResult DivRem(WideInteger dividend, WideInteger divisor)
            {
                if (IsZero(divisor))
                {
                    throw new DivideByZeroException();
                }
                if (Compare(dividend, divisor) < 0)
                {
                    return new DivisionResult(ZeroValue(), Copy(dividend));
                }
                var quotient = new WideInteger();
                var remainder = new WideInteger();
                var bitCount = BitLength(dividend);
                for (var bit = bitCount - 1; bit >= 0; bit--)
                {
                    ShiftLeftOne(ref remainder);
                    if ((dividend[bit >> 5] & (1U << (bit & 31))) != 0)
                    {
                        remainder[0] |= 1;
                    }
                    if (Compare(remainder, divisor) >= 0)
                    {
                        SubtractInPlace(ref remainder, divisor);
                        quotient[bit >> 5] |= 1U << (bit & 31);
                    }
                }
                return new DivisionResult(Trim(quotient), Trim(remainder));
            }

            private static void SubtractInPlace(ref WideInteger left, WideInteger right)
            {
                var borrow = 0UL;
                for (var index = 0; index < left.Length; index++)
                {
                    var subtrahend = right[index] + borrow;
                    var value = left[index];
                    left[index] = (uint)(value - subtrahend);
                    borrow = value < subtrahend ? 1UL : 0UL;
                }
            }

            private static void ShiftLeftOne(ref WideInteger value)
            {
                var carry = 0U;
                for (var index = 0; index < value.Length; index++)
                {
                    var next = value[index] >> 31;
                    value[index] = (value[index] << 1) | carry;
                    carry = next;
                }
                if (carry != 0)
                {
                    throw new OverflowException();
                }
            }

            private static int Compare(WideInteger left, WideInteger right)
            {
                var leftLength = SignificantLength(left);
                var rightLength = SignificantLength(right);
                if (leftLength != rightLength)
                {
                    return leftLength < rightLength ? -1 : 1;
                }
                var result = 0;
                for (var index = leftLength - 1; index >= 0; index--)
                {
                    if (result == 0 && left[index] != right[index])
                    {
                        result = left[index] < right[index] ? -1 : 1;
                    }
                }
                return result;
            }

            private static int BitLength(WideInteger value)
            {
                var length = SignificantLength(value);
                var high = value[length - 1];
                var bits = (length - 1) * 32;
                while (high != 0)
                {
                    high >>= 1;
                    bits++;
                }
                return bits;
            }

            private static bool Fits96(WideInteger value) => SignificantLength(value) <= 3;

            private static bool IsZero(WideInteger value) => SignificantLength(value) == 1 && value[0] == 0;

            private static WideInteger ZeroValue() => new();

            private static WideInteger FromUInt64(ulong value) => new()
            {
                Part0 = (uint)value,
                Part1 = (uint)(value >> 32),
            };

            private static WideInteger Copy(WideInteger value) => value;

            private static WideInteger CopyWithCapacity(WideInteger value, int capacity) => value;

            private static WideInteger Trim(WideInteger value) => value;

            private static int SignificantLength(WideInteger value)
            {
                var length = value.Length;
                for (var index = value.Length - 1; index > 0; index--)
                {
                    if (length == index + 1 && value[index] == 0)
                    {
                        length--;
                    }
                }
                return length;
            }
        }
    }
}
