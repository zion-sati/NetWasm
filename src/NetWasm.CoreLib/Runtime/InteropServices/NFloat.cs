// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// Upstream picks NFloat's native type with TARGET_32BIT. One NetWasm CoreLib serves wasm32 and wasm64, so NFloat keeps
// the native value's bits in an nint, four bytes on wasm32 and eight on wasm64 as upstream's float or double field, and
// each member that reads it has a double copy and a float copy that IntPtr.Size selects; the compiler folds the test.

namespace System.Runtime.InteropServices
{
    /// <summary>Defines an immutable value type that represents a floating type that has the same size as the native integer size.</summary>
    /// <remarks>It is meant to be used as an exchange type at the managed/unmanaged boundary to accurately represent in managed code unmanaged APIs that use a type alias for C or C++'s <c>float</c> on 32-bit platforms or <c>double</c> on 64-bit platforms, such as the CGFloat type in libraries provided by Apple.</remarks>
    [NonVersionable] // This only applies to field layout
    public readonly struct NFloat
        : IBinaryFloatingPointIeee754<NFloat>,
          IMinMaxValue<NFloat>,
          IUtf8SpanFormattable
    {
        private const NumberStyles DefaultNumberStyles = NumberStyles.Float | NumberStyles.AllowThousands;

        private readonly nint _bits;

        private double Value64 => BitConverter.Int64BitsToDouble(_bits);

        private float Value32 => BitConverter.Int32BitsToSingle((int)_bits);

        /// <summary>Constructs an instance from a 32-bit floating point value.</summary>
        /// <param name="value">The floating-point value.</param>
        [NonVersionable]
        public NFloat(float value)
        {
            _bits = IntPtr.Size == 8 ? (nint)BitConverter.DoubleToInt64Bits(value) : BitConverter.SingleToInt32Bits(value);
        }

        /// <summary>Constructs an instance from a 64-bit floating point value.</summary>
        /// <param name="value">The floating-point value.</param>
        [NonVersionable]
        public NFloat(double value)
        {
            _bits = IntPtr.Size == 8 ? (nint)BitConverter.DoubleToInt64Bits(value) : BitConverter.SingleToInt32Bits((float)value);
        }

        /// <summary>Represents the smallest positive NFloat value that is greater than zero.</summary>
        public static NFloat Epsilon
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? new NFloat(double.Epsilon)
                    : new NFloat(float.Epsilon);
        }

        /// <summary>Represents the largest finite value of a NFloat.</summary>
        public static NFloat MaxValue
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? new NFloat(double.MaxValue)
                    : new NFloat(float.MaxValue);
        }

        /// <summary>Represents the smallest finite value of a NFloat.</summary>
        public static NFloat MinValue
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? new NFloat(double.MinValue)
                    : new NFloat(float.MinValue);
        }

        /// <summary>Represents a value that is not a number (NaN).</summary>
        public static NFloat NaN
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? new NFloat(double.NaN)
                    : new NFloat(float.NaN);
        }

        /// <summary>Represents negative infinity.</summary>
        public static NFloat NegativeInfinity
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? new NFloat(double.NegativeInfinity)
                    : new NFloat(float.NegativeInfinity);
        }

        /// <summary>Represents positive infinity.</summary>
        public static NFloat PositiveInfinity
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? new NFloat(double.PositiveInfinity)
                    : new NFloat(float.PositiveInfinity);
        }

        /// <summary>Gets the size, in bytes, of an NFloat.</summary>
        public static int Size
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? sizeof(double)
                    : sizeof(float);
        }

        /// <summary>The underlying floating-point value of this instance.</summary>
        public double Value
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? Value64
                    : Value32;
        }

        //
        // Unary Arithmetic
        //

        /// <summary>Computes the unary plus of a value.</summary>
        /// <param name="value">The value for which to compute its unary plus.</param>
        /// <returns>The unary plus of <paramref name="value" />.</returns>
        [NonVersionable]
        public static NFloat operator +(NFloat value) => value;

        /// <summary>Computes the unary negation of a value.</summary>
        /// <param name="value">The value for which to compute its unary negation.</param>
        /// <returns>The unary negation of <paramref name="value" />.</returns>
        [NonVersionable]
        public static NFloat operator -(NFloat value) =>
            IntPtr.Size == 8
                ? new NFloat(-value.Value64)
                : new NFloat(-value.Value32);

        /// <summary>Increments a value.</summary>
        /// <param name="value">The value to increment.</param>
        /// <returns>The result of incrementing <paramref name="value" />.</returns>
        [NonVersionable]
        public static NFloat operator ++(NFloat value)
        {
            if (IntPtr.Size == 8)
            {
                double tmp = value.Value64;
                ++tmp;
                return new NFloat(tmp);
            }
            else
            {
                float tmp = value.Value32;
                ++tmp;
                return new NFloat(tmp);
            }
        }

        /// <summary>Decrements a value.</summary>
        /// <param name="value">The value to decrement.</param>
        /// <returns>The result of decrementing <paramref name="value" />.</returns>
        [NonVersionable]
        public static NFloat operator --(NFloat value)
        {
            if (IntPtr.Size == 8)
            {
                double tmp = value.Value64;
                --tmp;
                return new NFloat(tmp);
            }
            else
            {
                float tmp = value.Value32;
                --tmp;
                return new NFloat(tmp);
            }
        }

        //
        // Binary Arithmetic
        //

        /// <summary>Adds two values together to compute their sum.</summary>
        /// <param name="left">The value to which <paramref name="right" /> is added.</param>
        /// <param name="right">The value which is added to <paramref name="left" />.</param>
        /// <returns>The sum of <paramref name="left" /> and <paramref name="right" />.</returns>
        [NonVersionable]
        public static NFloat operator +(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? new NFloat(left.Value64 + right.Value64)
                : new NFloat(left.Value32 + right.Value32);

        /// <summary>Subtracts two values to compute their difference.</summary>
        /// <param name="left">The value from which <paramref name="right" /> is subtracted.</param>
        /// <param name="right">The value which is subtracted from <paramref name="left" />.</param>
        /// <returns>The difference of <paramref name="right" /> subtracted from <paramref name="left" />.</returns>
        [NonVersionable]
        public static NFloat operator -(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? new NFloat(left.Value64 - right.Value64)
                : new NFloat(left.Value32 - right.Value32);

        /// <summary>Multiplies two values together to compute their product.</summary>
        /// <param name="left">The value which <paramref name="right" /> multiplies.</param>
        /// <param name="right">The value which multiplies <paramref name="left" />.</param>
        /// <returns>The product of <paramref name="left" /> multiplied-by <paramref name="right" />.</returns>
        [NonVersionable]
        public static NFloat operator *(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? new NFloat(left.Value64 * right.Value64)
                : new NFloat(left.Value32 * right.Value32);

        /// <summary>Divides two values together to compute their quotient.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The quotient of <paramref name="left" /> divided-by <paramref name="right" />.</returns>
        [NonVersionable]
        public static NFloat operator /(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? new NFloat(left.Value64 / right.Value64)
                : new NFloat(left.Value32 / right.Value32);

        /// <summary>Divides two values together to compute their remainder.</summary>
        /// <param name="left">The value which <paramref name="right" /> divides.</param>
        /// <param name="right">The value which divides <paramref name="left" />.</param>
        /// <returns>The remainder of <paramref name="left" /> divided-by <paramref name="right" />.</returns>
        [NonVersionable]
        public static NFloat operator %(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? new NFloat(left.Value64 % right.Value64)
                : new NFloat(left.Value32 % right.Value32);

        //
        // Comparisons
        //

        /// <summary>Compares two values to determine equality.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        [NonVersionable]
        public static bool operator ==(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? left.Value64 == right.Value64
                : left.Value32 == right.Value32;

        /// <summary>Compares two values to determine inequality.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is not equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        [NonVersionable]
        public static bool operator !=(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? left.Value64 != right.Value64
                : left.Value32 != right.Value32;

        /// <summary>Compares two values to determine which is less.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is less than <paramref name="right" />; otherwise, <c>false</c>.</returns>
        [NonVersionable]
        public static bool operator <(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? left.Value64 < right.Value64
                : left.Value32 < right.Value32;

        /// <summary>Compares two values to determine which is less or equal.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is less than or equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        [NonVersionable]
        public static bool operator <=(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? left.Value64 <= right.Value64
                : left.Value32 <= right.Value32;

        /// <summary>Compares two values to determine which is greater.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is greater than <paramref name="right" />; otherwise, <c>false</c>.</returns>
        [NonVersionable]
        public static bool operator >(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? left.Value64 > right.Value64
                : left.Value32 > right.Value32;

        /// <summary>Compares two values to determine which is greater or equal.</summary>
        /// <param name="left">The value to compare with <paramref name="right" />.</param>
        /// <param name="right">The value to compare with <paramref name="left" />.</param>
        /// <returns><c>true</c> if <paramref name="left" /> is greater than or equal to <paramref name="right" />; otherwise, <c>false</c>.</returns>
        [NonVersionable]
        public static bool operator >=(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? left.Value64 >= right.Value64
                : left.Value32 >= right.Value32;

        //
        // Explicit Convert To NFloat
        //

        /// <summary>Explicitly converts a <see cref="decimal" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static explicit operator NFloat(decimal value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Explicitly converts a <see cref="double" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static explicit operator NFloat(double value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        //
        // Explicit Convert From NFloat
        //

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="byte" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="byte" /> value.</returns>
        [NonVersionable]
        public static explicit operator byte(NFloat value) =>
            IntPtr.Size == 8
                ? (byte)(value.Value64)
                : (byte)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="byte" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="byte" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="byte" />.</exception>
        [NonVersionable]
        public static explicit operator checked byte(NFloat value) =>
            IntPtr.Size == 8
                ? checked((byte)(value.Value64))
                : checked((byte)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="char" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="char" /> value.</returns>
        [NonVersionable]
        public static explicit operator char(NFloat value) =>
            IntPtr.Size == 8
                ? (char)(value.Value64)
                : (char)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="char" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="char" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="char" />.</exception>
        [NonVersionable]
        public static explicit operator checked char(NFloat value) =>
            IntPtr.Size == 8
                ? checked((char)(value.Value64))
                : checked((char)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="decimal" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="decimal" /> value.</returns>
        [NonVersionable]
        public static explicit operator decimal(NFloat value) =>
            IntPtr.Size == 8
                ? (decimal)(value.Value64)
                : (decimal)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="Half" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="Half" /> value.</returns>
        [NonVersionable]
        public static explicit operator Half(NFloat value) =>
            IntPtr.Size == 8
                ? (Half)(value.Value64)
                : (Half)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="short" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="short" /> value.</returns>
        [NonVersionable]
        public static explicit operator short(NFloat value) =>
            IntPtr.Size == 8
                ? (short)(value.Value64)
                : (short)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="short" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="short" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="short" />.</exception>
        [NonVersionable]
        public static explicit operator checked short(NFloat value) =>
            IntPtr.Size == 8
                ? checked((short)(value.Value64))
                : checked((short)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="int" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="int" /> value.</returns>
        [NonVersionable]
        public static explicit operator int(NFloat value) =>
            IntPtr.Size == 8
                ? (int)(value.Value64)
                : (int)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="int" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="int" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="int" />.</exception>
        [NonVersionable]
        public static explicit operator checked int(NFloat value) =>
            IntPtr.Size == 8
                ? checked((int)(value.Value64))
                : checked((int)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="long" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="long" /> value.</returns>
        [NonVersionable]
        public static explicit operator long(NFloat value) =>
            IntPtr.Size == 8
                ? (long)(value.Value64)
                : (long)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="long" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="long" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="long" />.</exception>
        [NonVersionable]
        public static explicit operator checked long(NFloat value) =>
            IntPtr.Size == 8
                ? checked((long)(value.Value64))
                : checked((long)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="Int128" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="Int128" /> value.</returns>
        [NonVersionable]
        public static explicit operator Int128(NFloat value) =>
            IntPtr.Size == 8
                ? (Int128)(value.Value64)
                : (Int128)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="Int128" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="Int128" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="Int128" />.</exception>
        [NonVersionable]
        public static explicit operator checked Int128(NFloat value) =>
            IntPtr.Size == 8
                ? checked((Int128)(value.Value64))
                : checked((Int128)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="IntPtr" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="IntPtr" /> value.</returns>
        [NonVersionable]
        public static explicit operator nint(NFloat value) =>
            IntPtr.Size == 8
                ? (nint)(value.Value64)
                : (nint)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="IntPtr" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="IntPtr" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="IntPtr" />.</exception>
        [NonVersionable]
        public static explicit operator checked nint(NFloat value) =>
            IntPtr.Size == 8
                ? checked((nint)(value.Value64))
                : checked((nint)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="sbyte" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="sbyte" /> value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator sbyte(NFloat value) =>
            IntPtr.Size == 8
                ? (sbyte)(value.Value64)
                : (sbyte)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="sbyte" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="sbyte" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="sbyte" />.</exception>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator checked sbyte(NFloat value) =>
            IntPtr.Size == 8
                ? checked((sbyte)(value.Value64))
                : checked((sbyte)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="float" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="float" /> value.</returns>
        [NonVersionable]
        public static explicit operator float(NFloat value) =>
            IntPtr.Size == 8
                ? (float)(value.Value64)
                : (float)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="ushort" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="ushort" /> value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator ushort(NFloat value) =>
            IntPtr.Size == 8
                ? (ushort)(value.Value64)
                : (ushort)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="ushort" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="ushort" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="ushort" />.</exception>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator checked ushort(NFloat value) =>
            IntPtr.Size == 8
                ? checked((ushort)(value.Value64))
                : checked((ushort)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="uint" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="uint" /> value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator uint(NFloat value) =>
            IntPtr.Size == 8
                ? (uint)(value.Value64)
                : (uint)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="uint" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="uint" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="uint" />.</exception>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator checked uint(NFloat value) =>
            IntPtr.Size == 8
                ? checked((uint)(value.Value64))
                : checked((uint)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="ulong" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="ulong" /> value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator ulong(NFloat value) =>
            IntPtr.Size == 8
                ? (ulong)(value.Value64)
                : (ulong)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="ulong" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="ulong" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="ulong" />.</exception>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator checked ulong(NFloat value) =>
            IntPtr.Size == 8
                ? checked((ulong)(value.Value64))
                : checked((ulong)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="UInt128" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="UInt128" /> value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator UInt128(NFloat value) =>
            IntPtr.Size == 8
                ? (UInt128)(value.Value64)
                : (UInt128)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="UInt128" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="UInt128" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="UInt128" />.</exception>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator checked UInt128(NFloat value) =>
            IntPtr.Size == 8
                ? checked((UInt128)(value.Value64))
                : checked((UInt128)(value.Value32));

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="UIntPtr" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="UIntPtr" /> value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator nuint(NFloat value) =>
            IntPtr.Size == 8
                ? (nuint)(value.Value64)
                : (nuint)(value.Value32);

        /// <summary>Explicitly converts a native-sized floating-point value to its nearest representable <see cref="UIntPtr" /> value, throwing an overflow exception for any values that fall outside the representable range.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="UIntPtr" /> value.</returns>
        /// <exception cref="OverflowException"><paramref name="value" /> is not representable by <see cref="UIntPtr" />.</exception>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator checked nuint(NFloat value) =>
            IntPtr.Size == 8
                ? checked((nuint)(value.Value64))
                : checked((nuint)(value.Value32));

        //
        // Implicit Convert To NFloat
        //

        /// <summary>Implicitly converts a <see cref="byte" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(byte value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="char" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(char value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="Half" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(Half value) => (NFloat)(float)value;

        /// <summary>Implicitly converts a <see cref="short" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(short value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="int" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(int value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="long" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(long value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Explicitly converts a <see cref="Int128" /> to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static explicit operator NFloat(Int128 value)
        {
            if (Int128.IsNegative(value))
            {
                value = -value;
                return -(NFloat)(UInt128)(value);
            }
            return (NFloat)(UInt128)(value);
        }

        /// <summary>Implicitly converts a <see cref="System.IntPtr" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(nint value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="sbyte" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static implicit operator NFloat(sbyte value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="float" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        public static implicit operator NFloat(float value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="ushort" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static implicit operator NFloat(ushort value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="uint" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static implicit operator NFloat(uint value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Implicitly converts a <see cref="ulong" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static implicit operator NFloat(ulong value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        /// <summary>Explicitly converts <see cref="UInt128"/> to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static explicit operator NFloat(UInt128 value) => (NFloat)(double)(value);

        /// <summary>Implicitly converts a <see cref="System.UIntPtr" /> value to its nearest representable native-sized floating-point value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable native-sized floating-point value.</returns>
        [NonVersionable]
        [CLSCompliant(false)]
        public static implicit operator NFloat(nuint value) =>
            IntPtr.Size == 8
                ? new NFloat((double)value)
                : new NFloat((float)value);

        //
        // Implicit Convert From NFloat
        //

        /// <summary>Implicitly converts a native-sized floating-point value to its nearest representable <see cref="double" /> value.</summary>
        /// <param name="value">The value to convert.</param>
        /// <returns><paramref name="value" /> converted to its nearest representable <see cref="double" /> value.</returns>
        public static implicit operator double(NFloat value) =>
            IntPtr.Size == 8
                ? (double)(value.Value64)
                : (double)(value.Value32);

        /// <summary>Determines whether the specified value is finite (zero, subnormal, or normal).</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is finite (zero, subnormal or normal); <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsFinite(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsFinite(value.Value64)
                : float.IsFinite(value.Value32);

        /// <summary>Determines whether the specified value is infinite (positive or negative infinity).</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is infinite (positive or negative infinity); <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsInfinity(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsInfinity(value.Value64)
                : float.IsInfinity(value.Value32);

        /// <summary>Determines whether the specified value is NaN (not a number).</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is NaN (not a number); <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsNaN(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsNaN(value.Value64)
                : float.IsNaN(value.Value32);

        /// <summary>Determines whether the specified value is negative.</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is negative; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsNegative(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsNegative(value.Value64)
                : float.IsNegative(value.Value32);

        /// <summary>Determines whether the specified value is negative infinity.</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is negative infinity; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsNegativeInfinity(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsNegativeInfinity(value.Value64)
                : float.IsNegativeInfinity(value.Value32);

        /// <summary>Determines whether the specified value is normal.</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is normal; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsNormal(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsNormal(value.Value64)
                : float.IsNormal(value.Value32);

        /// <summary>Determines whether the specified value is positive infinity.</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is positive infinity; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsPositiveInfinity(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsPositiveInfinity(value.Value64)
                : float.IsPositiveInfinity(value.Value32);

        /// <summary>Determines whether the specified value is subnormal.</summary>
        /// <param name="value">The floating-point value.</param>
        /// <returns><c>true</c> if the value is subnormal; <c>false</c> otherwise.</returns>
        [NonVersionable]
        public static bool IsSubnormal(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsSubnormal(value.Value64)
                : float.IsSubnormal(value.Value32);

        /// <summary>Converts the string representation of a number to its floating-point number equivalent.</summary>
        /// <param name="s">A string that contains the number to convert.</param>
        /// <returns>A floating-point number that is equivalent to the numeric value or symbol specified in <paramref name="s" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> does not represent a number in a valid format.</exception>
        public static NFloat Parse(string s)
        {
            if (IntPtr.Size == 8)
            {
                var result = double.Parse(s);
                return new NFloat(result);
            }
            else
            {
                var result = float.Parse(s);
                return new NFloat(result);
            }
        }

        /// <summary>Converts the string representation of a number in a specified style to its floating-point number equivalent.</summary>
        /// <param name="s">A string that contains the number to convert.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicate the style elements that can be present in <paramref name="s" />.</param>
        /// <returns>A floating-point number that is equivalent to the numeric value or symbol specified in <paramref name="s" />.</returns>
        /// <exception cref="ArgumentException">
        ///    <para><paramref name="style" /> is not a <see cref="NumberStyles" /> value.</para>
        ///    <para>-or-</para>
        ///    <para><paramref name="style" /> includes the <see cref="NumberStyles.AllowHexSpecifier" /> or <see cref="NumberStyles.AllowBinarySpecifier" /> value.</para>
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> does not represent a number in a valid format.</exception>
        public static NFloat Parse(string s, NumberStyles style)
        {
            if (IntPtr.Size == 8)
            {
                var result = double.Parse(s, style);
                return new NFloat(result);
            }
            else
            {
                var result = float.Parse(s, style);
                return new NFloat(result);
            }
        }

        /// <summary>Converts the string representation of a number in a specified culture-specific format to its floating-point number equivalent.</summary>
        /// <param name="s">A string that contains the number to convert.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>A floating-point number that is equivalent to the numeric value or symbol specified in <paramref name="s" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> does not represent a number in a valid format.</exception>
        public static NFloat Parse(string s, IFormatProvider? provider)
        {
            if (IntPtr.Size == 8)
            {
                var result = double.Parse(s, provider);
                return new NFloat(result);
            }
            else
            {
                var result = float.Parse(s, provider);
                return new NFloat(result);
            }
        }

        /// <summary>Converts the string representation of a number in a specified style and culture-specific format to its floating-point number equivalent.</summary>
        /// <param name="s">A string that contains the number to convert.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicate the style elements that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>A floating-point number that is equivalent to the numeric value or symbol specified in <paramref name="s" />.</returns>
        /// <exception cref="ArgumentException">
        ///    <para><paramref name="style" /> is not a <see cref="NumberStyles" /> value.</para>
        ///    <para>-or-</para>
        ///    <para><paramref name="style" /> includes the <see cref="NumberStyles.AllowHexSpecifier" /> or <see cref="NumberStyles.AllowBinarySpecifier" /> value.</para>
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
        /// <exception cref="FormatException"><paramref name="s" /> does not represent a number in a valid format.</exception>
        public static NFloat Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (IntPtr.Size == 8)
            {
                var result = double.Parse(s, style, provider);
                return new NFloat(result);
            }
            else
            {
                var result = float.Parse(s, style, provider);
                return new NFloat(result);
            }
        }

        /// <summary>Converts a character span that contains the string representation of a number in a specified style and culture-specific format to its floating-point number equivalent.</summary>
        /// <param name="s">A character span that contains the number to convert.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicate the style elements that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about <paramref name="s" />.</param>
        /// <returns>A floating-point number that is equivalent to the numeric value or symbol specified in <paramref name="s" />.</returns>
        /// <exception cref="ArgumentException">
        ///    <para><paramref name="style" /> is not a <see cref="NumberStyles" /> value.</para>
        ///    <para>-or-</para>
        ///    <para><paramref name="style" /> includes the <see cref="NumberStyles.AllowHexSpecifier" /> or <see cref="NumberStyles.AllowBinarySpecifier" /> value.</para>
        /// </exception>
        /// <exception cref="FormatException"><paramref name="s" /> does not represent a number in a valid format.</exception>
        public static NFloat Parse(ReadOnlySpan<char> s, NumberStyles style = DefaultNumberStyles, IFormatProvider? provider = null)
        {
            if (IntPtr.Size == 8)
            {
                var result = double.Parse(s, style, provider);
                return new NFloat(result);
            }
            else
            {
                var result = float.Parse(s, style, provider);
                return new NFloat(result);
            }
        }

        /// <summary>Tries to convert the string representation of a number to its floating-point number equivalent.</summary>
        /// <param name="s">A read-only character span that contains the number to convert.</param>
        /// <param name="result">When this method returns, contains a floating-point number equivalent of the numeric value or symbol contained in <paramref name="s" /> if the conversion succeeded or zero if the conversion failed. The conversion fails if the <paramref name="s" /> is <c>null</c>, <see cref="string.Empty" />, or is not in a valid format. This parameter is passed uninitialized; any value originally supplied in result will be overwritten.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was converted successfully; otherwise, false.</returns>
        public static bool TryParse([NotNullWhen(true)] string? s, out NFloat result)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                return double.TryParse(s, out Unsafe.As<NFloat, double>(ref result));
            }
            else
            {
                Unsafe.SkipInit(out result);
                return float.TryParse(s, out Unsafe.As<NFloat, float>(ref result));
            }
        }

        /// <summary>Tries to convert a character span containing the string representation of a number to its floating-point number equivalent.</summary>
        /// <param name="s">A read-only character span that contains the number to convert.</param>
        /// <param name="result">When this method returns, contains a floating-point number equivalent of the numeric value or symbol contained in <paramref name="s" /> if the conversion succeeded or zero if the conversion failed. The conversion fails if the <paramref name="s" /> is <see cref="ReadOnlySpan{T}.Empty" /> or is not in a valid format. This parameter is passed uninitialized; any value originally supplied in result will be overwritten.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was converted successfully; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<char> s, out NFloat result)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                return double.TryParse(s, out Unsafe.As<NFloat, double>(ref result));
            }
            else
            {
                Unsafe.SkipInit(out result);
                return float.TryParse(s, out Unsafe.As<NFloat, float>(ref result));
            }
        }

        /// <summary>Tries to convert a UTF-8 character span containing the string representation of a number to its floating-point number equivalent.</summary>
        /// <param name="utf8Text">A read-only UTF-8 character span that contains the number to convert.</param>
        /// <param name="result">When this method returns, contains a floating-point number equivalent of the numeric value or symbol contained in <paramref name="utf8Text" /> if the conversion succeeded or zero if the conversion failed. The conversion fails if the <paramref name="utf8Text" /> is <see cref="ReadOnlySpan{T}.Empty" /> or is not in a valid format. This parameter is passed uninitialized; any value originally supplied in result will be overwritten.</param>
        /// <returns><c>true</c> if <paramref name="utf8Text" /> was converted successfully; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out NFloat result)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                return double.TryParse(utf8Text, out Unsafe.As<NFloat, double>(ref result));
            }
            else
            {
                Unsafe.SkipInit(out result);
                return float.TryParse(utf8Text, out Unsafe.As<NFloat, float>(ref result));
            }
        }

        /// <summary>Tries to convert the string representation of a number in a specified style and culture-specific format to its floating-point number equivalent.</summary>
        /// <param name="s">A read-only character span that contains the number to convert.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicate the style elements that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about <paramref name="s" />.</param>
        /// <param name="result">When this method returns, contains a floating-point number equivalent of the numeric value or symbol contained in <paramref name="s" /> if the conversion succeeded or zero if the conversion failed. The conversion fails if the <paramref name="s" /> is <c>null</c>, <see cref="string.Empty" />, or is not in a format compliant with <paramref name="style" />, or if <paramref name="style" /> is not a valid combination of <see cref="NumberStyles" /> enumeration constants. This parameter is passed uninitialized; any value originally supplied in result will be overwritten.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was converted successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentException">
        ///    <para><paramref name="style" /> is not a <see cref="NumberStyles" /> value.</para>
        ///    <para>-or-</para>
        ///    <para><paramref name="style" /> includes the <see cref="NumberStyles.AllowHexSpecifier" /> or <see cref="NumberStyles.AllowBinarySpecifier" /> value.</para>
        /// </exception>
        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out NFloat result)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                return double.TryParse(s, style, provider, out Unsafe.As<NFloat, double>(ref result));
            }
            else
            {
                Unsafe.SkipInit(out result);
                return float.TryParse(s, style, provider, out Unsafe.As<NFloat, float>(ref result));
            }
        }

        /// <summary>Tries to convert a character span containing the string representation of a number in a specified style and culture-specific format to its floating-point number equivalent.</summary>
        /// <param name="s">A read-only character span that contains the number to convert.</param>
        /// <param name="style">A bitwise combination of enumeration values that indicate the style elements that can be present in <paramref name="s" />.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information about <paramref name="s" />.</param>
        /// <param name="result">When this method returns, contains a floating-point number equivalent of the numeric value or symbol contained in <paramref name="s" /> if the conversion succeeded or zero if the conversion failed. The conversion fails if the <paramref name="s" /> is <see cref="string.Empty" /> or is not in a format compliant with <paramref name="style" />, or if <paramref name="style" /> is not a valid combination of <see cref="NumberStyles" /> enumeration constants. This parameter is passed uninitialized; any value originally supplied in result will be overwritten.</param>
        /// <returns><c>true</c> if <paramref name="s" /> was converted successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentException">
        ///    <para><paramref name="style" /> is not a <see cref="NumberStyles" /> value.</para>
        ///    <para>-or-</para>
        ///    <para><paramref name="style" /> includes the <see cref="NumberStyles.AllowHexSpecifier" /> or <see cref="NumberStyles.AllowBinarySpecifier" /> value.</para>
        /// </exception>
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out NFloat result)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                return double.TryParse(s, style, provider, out Unsafe.As<NFloat, double>(ref result));
            }
            else
            {
                Unsafe.SkipInit(out result);
                return float.TryParse(s, style, provider, out Unsafe.As<NFloat, float>(ref result));
            }
        }

        /// <summary>Compares this instance to a specified object and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified object.</summary>
        /// <param name="obj">An object to compare, or <c>null</c>.</param>
        /// <returns>
        ///     <para>A signed number indicating the relative values of this instance and <paramref name="obj" />.</para>
        ///     <list type="table">
        ///         <listheader>
        ///             <term>Return Value</term>
        ///             <description>Description</description>
        ///         </listheader>
        ///         <item>
        ///             <term>Less than zero</term>
        ///             <description>This instance is less than <paramref name="obj" />, or this instance is not a number and <paramref name="obj" /> is a number.</description>
        ///         </item>
        ///         <item>
        ///             <term>Zero</term>
        ///             <description>This instance is equal to <paramref name="obj" />, or both this instance and <paramref name="obj" /> are not a number.</description>
        ///         </item>
        ///         <item>
        ///             <term>Greater than zero</term>
        ///             <description>This instance is greater than <paramref name="obj" />, or this instance is a number and <paramref name="obj" /> is not a number or <paramref name="obj" /> is <c>null</c>.</description>
        ///         </item>
        ///     </list>
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="obj" /> is not a <see cref="NFloat" />.</exception>
        public int CompareTo(object? obj)
        {
            if (obj is NFloat other)
            {
                return CompareTo(other);
            }
            else if (obj is null)
            {
                return 1;
            }

            throw new ArgumentException(SR.Arg_MustBeNFloat);
        }

        /// <summary>Compares this instance to a specified floating-point number and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified floating-point number.</summary>
        /// <param name="other">A floating-point number to compare.</param>
        /// <returns>
        ///     <para>A signed number indicating the relative values of this instance and <paramref name="other" />.</para>
        ///     <list type="table">
        ///         <listheader>
        ///             <term>Return Value</term>
        ///             <description>Description</description>
        ///         </listheader>
        ///         <item>
        ///             <term>Less than zero</term>
        ///             <description>This instance is less than <paramref name="other" />, or this instance is not a number and <paramref name="other" /> is a number.</description>
        ///         </item>
        ///         <item>
        ///             <term>Zero</term>
        ///             <description>This instance is equal to <paramref name="other" />, or both this instance and <paramref name="other" /> are not a number.</description>
        ///         </item>
        ///         <item>
        ///             <term>Greater than zero</term>
        ///             <description>This instance is greater than <paramref name="other" />, or this instance is a number and <paramref name="other" /> is not a number.</description>
        ///         </item>
        ///     </list>
        /// </returns>
        public int CompareTo(NFloat other) =>
            IntPtr.Size == 8
                ? Value64.CompareTo(other.Value64)
                : Value32.CompareTo(other.Value32);

        /// <summary>Returns a value indicating whether this instance is equal to a specified object.</summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns><c>true</c> if <paramref name="obj"/> is an instance of <see cref="NFloat"/> and equals the value of this instance; otherwise, <c>false</c>.</returns>
        public override bool Equals([NotNullWhen(true)] object? obj) => (obj is NFloat other) && Equals(other);

        /// <summary>Returns a value indicating whether this instance is equal to a specified <see cref="NFloat" /> value.</summary>
        /// <param name="other">An <see cref="NFloat"/> value to compare to this instance.</param>
        /// <returns><c>true</c> if <paramref name="other"/> has the same value as this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(NFloat other) =>
            IntPtr.Size == 8
                ? Value64.Equals(other.Value64)
                : Value32.Equals(other.Value32);

        /// <summary>Returns the hash code for this instance.</summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode() =>
            IntPtr.Size == 8
                ? Value64.GetHashCode()
                : Value32.GetHashCode();

        /// <summary>Converts the numeric value of this instance to its equivalent string representation.</summary>
        /// <returns>The string representation of the value of this instance.</returns>
        public override string ToString() =>
            IntPtr.Size == 8
                ? Value64.ToString()
                : Value32.ToString();

        /// <summary>Converts the numeric value of this instance to its equivalent string representation using the specified format.</summary>
        /// <param name="format">A numeric format string.</param>
        /// <returns>The string representation of the value of this instance as specified by <paramref name="format" />.</returns>
        /// <exception cref="FormatException"><paramref name="format" /> is invalid.</exception>
        public string ToString([StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format) =>
            IntPtr.Size == 8
                ? Value64.ToString(format)
                : Value32.ToString(format);

        /// <summary>Converts the numeric value of this instance to its equivalent string representation using the specified culture-specific format information.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <returns>The string representation of the value of this instance as specified by <paramref name="provider" />.</returns>
        public string ToString(IFormatProvider? provider) =>
            IntPtr.Size == 8
                ? Value64.ToString(provider)
                : Value32.ToString(provider);

        /// <summary>Converts the numeric value of this instance to its equivalent string representation using the specified format and culture-specific format information.</summary>
        /// <param name="format">A numeric format string.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <returns>The string representation of the value of this instance as specified by <paramref name="format" /> and <paramref name="provider" />.</returns>
        /// <exception cref="FormatException"><paramref name="format" /> is invalid.</exception>
        public string ToString([StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format, IFormatProvider? provider) =>
            IntPtr.Size == 8
                ? Value64.ToString(format, provider)
                : Value32.ToString(format, provider);

        /// <summary>Tries to format the value of the current instance into the provided span of characters.</summary>
        /// <param name="destination">The span in which to write this instance's value formatted as a span of characters.</param>
        /// <param name="charsWritten">When this method returns, contains the number of characters that were written in <paramref name="destination" />.</param>
        /// <param name="format">A span containing the characters that represent a standard or custom format string that defines the acceptable format for <paramref name="destination" />.</param>
        /// <param name="provider">An optional object that supplies culture-specific formatting information for <paramref name="destination" />.</param>
        /// <returns><c>true</c> if the formatting was successful; otherwise, <c>false</c>.</returns>
        public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.NumericFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            IntPtr.Size == 8
                ? Value64.TryFormat(destination, out charsWritten, format, provider)
                : Value32.TryFormat(destination, out charsWritten, format, provider);

        /// <inheritdoc cref="IUtf8SpanFormattable.TryFormat" />
        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.NumericFormat)] ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
            IntPtr.Size == 8
                ? Value64.TryFormat(utf8Destination, out bytesWritten, format, provider)
                : Value32.TryFormat(utf8Destination, out bytesWritten, format, provider);

        //
        // IAdditiveIdentity
        //

        /// <inheritdoc cref="IAdditiveIdentity{TSelf, TResult}.AdditiveIdentity" />
        static NFloat IAdditiveIdentity<NFloat, NFloat>.AdditiveIdentity =>
            IntPtr.Size == 8
                ? new NFloat(0.0)
                : new NFloat(0.0f);

        //
        // IBinaryNumber
        //

        /// <inheritdoc cref="IBinaryNumber{TSelf}.AllBitsSet" />
        static NFloat IBinaryNumber<NFloat>.AllBitsSet
        {
            [NonVersionable]
            get =>
                IntPtr.Size == 8
                    ? (NFloat)BitConverter.UInt64BitsToDouble(0xFFFF_FFFF_FFFF_FFFF)
                    : BitConverter.UInt32BitsToSingle(0xFFFF_FFFF);
        }

        /// <inheritdoc cref="IBinaryNumber{TSelf}.IsPow2(TSelf)" />
        public static bool IsPow2(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsPow2(value.Value64)
                : float.IsPow2(value.Value32);

        /// <inheritdoc cref="IBinaryNumber{TSelf}.Log2(TSelf)" />
        public static NFloat Log2(NFloat value) =>
            IntPtr.Size == 8
                ? new NFloat(double.Log2(value.Value64))
                : new NFloat(float.Log2(value.Value32));

        //
        // IBitwiseOperators
        //

        /// <inheritdoc cref="IBitwiseOperators{TSelf, TOther, TResult}.op_BitwiseAnd(TSelf, TOther)" />
        static NFloat IBitwiseOperators<NFloat, NFloat, NFloat>.operator &(NFloat left, NFloat right)
        {
            if (IntPtr.Size == 8)
            {
                ulong bits = BitConverter.DoubleToUInt64Bits(left.Value64) & BitConverter.DoubleToUInt64Bits(right.Value64);
                double result = BitConverter.UInt64BitsToDouble(bits);
                return new NFloat(result);
            }
            else
            {
                uint bits = BitConverter.SingleToUInt32Bits(left.Value32) & BitConverter.SingleToUInt32Bits(right.Value32);
                float result = BitConverter.UInt32BitsToSingle(bits);
                return new NFloat(result);
            }
        }

        /// <inheritdoc cref="IBitwiseOperators{TSelf, TOther, TResult}.op_BitwiseOr(TSelf, TOther)" />
        static NFloat IBitwiseOperators<NFloat, NFloat, NFloat>.operator |(NFloat left, NFloat right)
        {
            if (IntPtr.Size == 8)
            {
                ulong bits = BitConverter.DoubleToUInt64Bits(left.Value64) | BitConverter.DoubleToUInt64Bits(right.Value64);
                double result = BitConverter.UInt64BitsToDouble(bits);
                return new NFloat(result);
            }
            else
            {
                uint bits = BitConverter.SingleToUInt32Bits(left.Value32) | BitConverter.SingleToUInt32Bits(right.Value32);
                float result = BitConverter.UInt32BitsToSingle(bits);
                return new NFloat(result);
            }
        }

        /// <inheritdoc cref="IBitwiseOperators{TSelf, TOther, TResult}.op_ExclusiveOr(TSelf, TOther)" />
        static NFloat IBitwiseOperators<NFloat, NFloat, NFloat>.operator ^(NFloat left, NFloat right)
        {
            if (IntPtr.Size == 8)
            {
                ulong bits = BitConverter.DoubleToUInt64Bits(left.Value64) ^ BitConverter.DoubleToUInt64Bits(right.Value64);
                double result = BitConverter.UInt64BitsToDouble(bits);
                return new NFloat(result);
            }
            else
            {
                uint bits = BitConverter.SingleToUInt32Bits(left.Value32) ^ BitConverter.SingleToUInt32Bits(right.Value32);
                float result = BitConverter.UInt32BitsToSingle(bits);
                return new NFloat(result);
            }
        }

        /// <inheritdoc cref="IBitwiseOperators{TSelf, TOther, TResult}.op_OnesComplement(TSelf)" />
        static NFloat IBitwiseOperators<NFloat, NFloat, NFloat>.operator ~(NFloat value)
        {
            if (IntPtr.Size == 8)
            {
                ulong bits = ~BitConverter.DoubleToUInt64Bits(value.Value64);
                double result = BitConverter.UInt64BitsToDouble(bits);
                return new NFloat(result);
            }
            else
            {
                uint bits = ~BitConverter.SingleToUInt32Bits(value.Value32);
                float result = BitConverter.UInt32BitsToSingle(bits);
                return new NFloat(result);
            }
        }

        //
        // IExponentialFunctions
        //

        /// <inheritdoc cref="IExponentialFunctions{TSelf}.Exp" />
        public static NFloat Exp(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Exp(x.Value64))
                : new NFloat(float.Exp(x.Value32));

        /// <inheritdoc cref="IExponentialFunctions{TSelf}.ExpM1(TSelf)" />
        public static NFloat ExpM1(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.ExpM1(x.Value64))
                : new NFloat(float.ExpM1(x.Value32));

        /// <inheritdoc cref="IExponentialFunctions{TSelf}.Exp2(TSelf)" />
        public static NFloat Exp2(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Exp2(x.Value64))
                : new NFloat(float.Exp2(x.Value32));

        /// <inheritdoc cref="IExponentialFunctions{TSelf}.Exp2M1(TSelf)" />
        public static NFloat Exp2M1(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Exp2M1(x.Value64))
                : new NFloat(float.Exp2M1(x.Value32));

        /// <inheritdoc cref="IExponentialFunctions{TSelf}.Exp10(TSelf)" />
        public static NFloat Exp10(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Exp10(x.Value64))
                : new NFloat(float.Exp10(x.Value32));

        /// <inheritdoc cref="IExponentialFunctions{TSelf}.Exp10M1(TSelf)" />
        public static NFloat Exp10M1(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Exp10M1(x.Value64))
                : new NFloat(float.Exp10M1(x.Value32));

        //
        // IFloatingPoint
        //

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Ceiling(TSelf)" />
        public static NFloat Ceiling(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Ceiling(x.Value64))
                : new NFloat(float.Ceiling(x.Value32));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.ConvertToInteger{TInteger}(TSelf)" />
        public static TInteger ConvertToInteger<TInteger>(NFloat value)
            where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

        /// <inheritdoc cref="IFloatingPoint{TSelf}.ConvertToIntegerNative{TInteger}(TSelf)" />
        public static TInteger ConvertToIntegerNative<TInteger>(NFloat value)
            where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Floor(TSelf)" />
        public static NFloat Floor(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Floor(x.Value64))
                : new NFloat(float.Floor(x.Value32));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Round(TSelf)" />
        public static NFloat Round(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Round(x.Value64))
                : new NFloat(float.Round(x.Value32));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Round(TSelf, int)" />
        public static NFloat Round(NFloat x, int digits) =>
            IntPtr.Size == 8
                ? new NFloat(double.Round(x.Value64, digits))
                : new NFloat(float.Round(x.Value32, digits));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Round(TSelf, MidpointRounding)" />
        public static NFloat Round(NFloat x, MidpointRounding mode) =>
            IntPtr.Size == 8
                ? new NFloat(double.Round(x.Value64, mode))
                : new NFloat(float.Round(x.Value32, mode));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Round(TSelf, int, MidpointRounding)" />
        public static NFloat Round(NFloat x, int digits, MidpointRounding mode) =>
            IntPtr.Size == 8
                ? new NFloat(double.Round(x.Value64, digits, mode))
                : new NFloat(float.Round(x.Value32, digits, mode));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.Truncate(TSelf)" />
        public static NFloat Truncate(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Truncate(x.Value64))
                : new NFloat(float.Truncate(x.Value32));

        /// <inheritdoc cref="IFloatingPoint{TSelf}.GetExponentByteCount()" />
        int IFloatingPoint<NFloat>.GetExponentByteCount() =>
            IntPtr.Size == 8
                ? sizeof(short)
                : sizeof(sbyte);

        /// <inheritdoc cref="IFloatingPoint{TSelf}.GetExponentShortestBitLength()" />
        int IFloatingPoint<NFloat>.GetExponentShortestBitLength()
        {
            if (IntPtr.Size == 8)
            {
                short exponent = Value64.Exponent;

                if (exponent >= 0)
                {
                    return (sizeof(short) * 8) - short.LeadingZeroCount(exponent);
                }
                else
                {
                    return (sizeof(short) * 8) + 1 - short.LeadingZeroCount((short)(~exponent));
                }
            }
            else
            {
                sbyte exponent = Value32.Exponent;

                if (exponent >= 0)
                {
                    return (sizeof(sbyte) * 8) - sbyte.LeadingZeroCount(exponent);
                }
                else
                {
                    return (sizeof(sbyte) * 8) + 1 - sbyte.LeadingZeroCount((sbyte)(~exponent));
                }
            }
        }

        /// <inheritdoc cref="IFloatingPoint{TSelf}.GetSignificandByteCount()" />
        int IFloatingPoint<NFloat>.GetSignificandByteCount() =>
            IntPtr.Size == 8
                ? sizeof(ulong)
                : sizeof(uint);

        /// <inheritdoc cref="IFloatingPoint{TSelf}.GetSignificandBitLength()" />
        int IFloatingPoint<NFloat>.GetSignificandBitLength()
        {
            return IntPtr.Size == 8 ? 53 : 24;
        }

        /// <inheritdoc cref="IFloatingPoint{TSelf}.TryWriteExponentBigEndian(Span{byte}, out int)" />
        bool IFloatingPoint<NFloat>.TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten)
        {
            if (IntPtr.Size == 8)
            {
                return Value64.TryWriteExponentBigEndian(destination, out bytesWritten);
            }
            else
            {
                return Value32.TryWriteExponentBigEndian(destination, out bytesWritten);
            }
        }

        /// <inheritdoc cref="IFloatingPoint{TSelf}.TryWriteExponentLittleEndian(Span{byte}, out int)" />
        bool IFloatingPoint<NFloat>.TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten)
        {
            if (IntPtr.Size == 8)
            {
                return Value64.TryWriteExponentLittleEndian(destination, out bytesWritten);
            }
            else
            {
                return Value32.TryWriteExponentLittleEndian(destination, out bytesWritten);
            }
        }

        /// <inheritdoc cref="IFloatingPoint{TSelf}.TryWriteSignificandBigEndian(Span{byte}, out int)" />
        bool IFloatingPoint<NFloat>.TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten)
        {
            if (IntPtr.Size == 8)
            {
                return Value64.TryWriteSignificandBigEndian(destination, out bytesWritten);
            }
            else
            {
                return Value32.TryWriteSignificandBigEndian(destination, out bytesWritten);
            }
        }

        /// <inheritdoc cref="IFloatingPoint{TSelf}.TryWriteSignificandLittleEndian(Span{byte}, out int)" />
        bool IFloatingPoint<NFloat>.TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten)
        {
            if (IntPtr.Size == 8)
            {
                return Value64.TryWriteSignificandLittleEndian(destination, out bytesWritten);
            }
            else
            {
                return Value32.TryWriteSignificandLittleEndian(destination, out bytesWritten);
            }
        }

        //
        // IFloatingPointConstants
        //

        /// <inheritdoc cref="IFloatingPointConstants{TSelf}.E" />
        public static NFloat E =>
            IntPtr.Size == 8
                ? new NFloat(double.E)
                : new NFloat(float.E);

        /// <inheritdoc cref="IFloatingPointConstants{TSelf}.Pi" />
        public static NFloat Pi =>
            IntPtr.Size == 8
                ? new NFloat(double.Pi)
                : new NFloat(float.Pi);

        /// <inheritdoc cref="IFloatingPointConstants{TSelf}.Tau" />
        public static NFloat Tau =>
            IntPtr.Size == 8
                ? new NFloat(double.Tau)
                : new NFloat(float.Tau);

        //
        // IFloatingPointIeee754
        //

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.NegativeZero" />
        public static NFloat NegativeZero =>
            IntPtr.Size == 8
                ? new NFloat(double.NegativeZero)
                : new NFloat(float.NegativeZero);

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.Atan2(TSelf, TSelf)" />
        public static NFloat Atan2(NFloat y, NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Atan2(y.Value64, x.Value64))
                : new NFloat(float.Atan2(y.Value32, x.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.Atan2Pi(TSelf, TSelf)" />
        public static NFloat Atan2Pi(NFloat y, NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Atan2Pi(y.Value64, x.Value64))
                : new NFloat(float.Atan2Pi(y.Value32, x.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.BitDecrement(TSelf)" />
        public static NFloat BitDecrement(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.BitDecrement(x.Value64))
                : new NFloat(float.BitDecrement(x.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.BitIncrement(TSelf)" />
        public static NFloat BitIncrement(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.BitIncrement(x.Value64))
                : new NFloat(float.BitIncrement(x.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.FusedMultiplyAdd(TSelf, TSelf, TSelf)" />
        public static NFloat FusedMultiplyAdd(NFloat left, NFloat right, NFloat addend) =>
            IntPtr.Size == 8
                ? new NFloat(double.FusedMultiplyAdd(left.Value64, right.Value64, addend.Value64))
                : new NFloat(float.FusedMultiplyAdd(left.Value32, right.Value32, addend.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.Ieee754Remainder(TSelf, TSelf)" />
        public static NFloat Ieee754Remainder(NFloat left, NFloat right) =>
            IntPtr.Size == 8
                ? new NFloat(double.Ieee754Remainder(left.Value64, right.Value64))
                : new NFloat(float.Ieee754Remainder(left.Value32, right.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.ILogB(TSelf)" />
        public static int ILogB(NFloat x) =>
            IntPtr.Size == 8
                ? double.ILogB(x.Value64)
                : float.ILogB(x.Value32);

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.Lerp(TSelf, TSelf, TSelf)" />
        public static NFloat Lerp(NFloat value1, NFloat value2, NFloat amount) =>
            IntPtr.Size == 8
                ? new NFloat(double.Lerp(value1.Value64, value2.Value64, amount.Value64))
                : new NFloat(float.Lerp(value1.Value32, value2.Value32, amount.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.ReciprocalEstimate(TSelf)" />
        public static NFloat ReciprocalEstimate(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.ReciprocalEstimate(x.Value64))
                : new NFloat(float.ReciprocalEstimate(x.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.ReciprocalSqrtEstimate(TSelf)" />
        public static NFloat ReciprocalSqrtEstimate(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.ReciprocalSqrtEstimate(x.Value64))
                : new NFloat(float.ReciprocalSqrtEstimate(x.Value32));

        /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.ScaleB(TSelf, int)" />
        public static NFloat ScaleB(NFloat x, int n) =>
            IntPtr.Size == 8
                ? new NFloat(double.ScaleB(x.Value64, n))
                : new NFloat(float.ScaleB(x.Value32, n));

        // /// <inheritdoc cref="IFloatingPointIeee754{TSelf}.Compound(TSelf, TSelf)" />
        // public static NFloat Compound(NFloat x, NFloat n) => new NFloat(NativeType.Compound(x._value, n._value));

        //
        // IHyperbolicFunctions
        //

        /// <inheritdoc cref="IHyperbolicFunctions{TSelf}.Acosh(TSelf)" />
        public static NFloat Acosh(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Acosh(x.Value64))
                : new NFloat(float.Acosh(x.Value32));

        /// <inheritdoc cref="IHyperbolicFunctions{TSelf}.Asinh(TSelf)" />
        public static NFloat Asinh(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Asinh(x.Value64))
                : new NFloat(float.Asinh(x.Value32));

        /// <inheritdoc cref="IHyperbolicFunctions{TSelf}.Atanh(TSelf)" />
        public static NFloat Atanh(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Atanh(x.Value64))
                : new NFloat(float.Atanh(x.Value32));

        /// <inheritdoc cref="IHyperbolicFunctions{TSelf}.Cosh(TSelf)" />
        public static NFloat Cosh(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Cosh(x.Value64))
                : new NFloat(float.Cosh(x.Value32));

        /// <inheritdoc cref="IHyperbolicFunctions{TSelf}.Sinh(TSelf)" />
        public static NFloat Sinh(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Sinh(x.Value64))
                : new NFloat(float.Sinh(x.Value32));

        /// <inheritdoc cref="IHyperbolicFunctions{TSelf}.Tanh(TSelf)" />
        public static NFloat Tanh(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Tanh(x.Value64))
                : new NFloat(float.Tanh(x.Value32));

        //
        // ILogarithmicFunctions
        //

        /// <inheritdoc cref="ILogarithmicFunctions{TSelf}.Log(TSelf)" />
        public static NFloat Log(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Log(x.Value64))
                : new NFloat(float.Log(x.Value32));

        /// <inheritdoc cref="ILogarithmicFunctions{TSelf}.Log(TSelf, TSelf)" />
        public static NFloat Log(NFloat x, NFloat newBase) =>
            IntPtr.Size == 8
                ? new NFloat(double.Log(x.Value64, newBase.Value64))
                : new NFloat(float.Log(x.Value32, newBase.Value32));

        /// <inheritdoc cref="ILogarithmicFunctions{TSelf}.LogP1(TSelf)" />
        public static NFloat LogP1(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.LogP1(x.Value64))
                : new NFloat(float.LogP1(x.Value32));

        /// <inheritdoc cref="ILogarithmicFunctions{TSelf}.Log2P1(TSelf)" />
        public static NFloat Log2P1(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Log2P1(x.Value64))
                : new NFloat(float.Log2P1(x.Value32));

        /// <inheritdoc cref="ILogarithmicFunctions{TSelf}.Log10(TSelf)" />
        public static NFloat Log10(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Log10(x.Value64))
                : new NFloat(float.Log10(x.Value32));

        /// <inheritdoc cref="ILogarithmicFunctions{TSelf}.Log10P1(TSelf)" />
        public static NFloat Log10P1(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Log10P1(x.Value64))
                : new NFloat(float.Log10P1(x.Value32));

        //
        // IMultiplicativeIdentity
        //

        /// <inheritdoc cref="IMultiplicativeIdentity{TSelf, TResult}.MultiplicativeIdentity" />
        static NFloat IMultiplicativeIdentity<NFloat, NFloat>.MultiplicativeIdentity =>
            IntPtr.Size == 8
                ? new NFloat(1.0)
                : new NFloat(1.0f);

        //
        // INumber
        //

        /// <inheritdoc cref="INumber{TSelf}.Clamp(TSelf, TSelf, TSelf)" />
        public static NFloat Clamp(NFloat value, NFloat min, NFloat max) =>
            IntPtr.Size == 8
                ? new NFloat(double.Clamp(value.Value64, min.Value64, max.Value64))
                : new NFloat(float.Clamp(value.Value32, min.Value32, max.Value32));

        /// <inheritdoc cref="INumber{TSelf}.ClampNative(TSelf, TSelf, TSelf)" />
        public static NFloat ClampNative(NFloat value, NFloat min, NFloat max) =>
            IntPtr.Size == 8
                ? new NFloat(double.ClampNative(value.Value64, min.Value64, max.Value64))
                : new NFloat(float.ClampNative(value.Value32, min.Value32, max.Value32));

        /// <inheritdoc cref="INumber{TSelf}.CopySign(TSelf, TSelf)" />
        public static NFloat CopySign(NFloat value, NFloat sign) =>
            IntPtr.Size == 8
                ? new NFloat(double.CopySign(value.Value64, sign.Value64))
                : new NFloat(float.CopySign(value.Value32, sign.Value32));

        /// <inheritdoc cref="INumber{TSelf}.Max(TSelf, TSelf)" />
        public static NFloat Max(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.Max(x.Value64, y.Value64))
                : new NFloat(float.Max(x.Value32, y.Value32));

        /// <inheritdoc cref="INumber{TSelf}.MaxNative(TSelf, TSelf)" />
        public static NFloat MaxNative(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MaxNative(x.Value64, y.Value64))
                : new NFloat(float.MaxNative(x.Value32, y.Value32));

        /// <inheritdoc cref="INumber{TSelf}.MaxNumber(TSelf, TSelf)" />
        public static NFloat MaxNumber(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MaxNumber(x.Value64, y.Value64))
                : new NFloat(float.MaxNumber(x.Value32, y.Value32));

        /// <inheritdoc cref="INumber{TSelf}.Min(TSelf, TSelf)" />
        public static NFloat Min(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.Min(x.Value64, y.Value64))
                : new NFloat(float.Min(x.Value32, y.Value32));

        /// <inheritdoc cref="INumber{TSelf}.MinNative(TSelf, TSelf)" />
        public static NFloat MinNative(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MinNative(x.Value64, y.Value64))
                : new NFloat(float.MinNative(x.Value32, y.Value32));

        /// <inheritdoc cref="INumber{TSelf}.MinNumber(TSelf, TSelf)" />
        public static NFloat MinNumber(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MinNumber(x.Value64, y.Value64))
                : new NFloat(float.MinNumber(x.Value32, y.Value32));

        /// <inheritdoc cref="INumber{TSelf}.Sign(TSelf)" />
        public static int Sign(NFloat value) =>
            IntPtr.Size == 8
                ? double.Sign(value.Value64)
                : float.Sign(value.Value32);

        //
        // INumberBase
        //

        /// <inheritdoc cref="INumberBase{TSelf}.One" />
        static NFloat INumberBase<NFloat>.One =>
            IntPtr.Size == 8
                ? new NFloat(double.One)
                : new NFloat(float.One);

        /// <inheritdoc cref="INumberBase{TSelf}.Radix" />
        static int INumberBase<NFloat>.Radix => 2;

        /// <inheritdoc cref="INumberBase{TSelf}.Zero" />
        static NFloat INumberBase<NFloat>.Zero =>
            IntPtr.Size == 8
                ? new NFloat(double.Zero)
                : new NFloat(float.Zero);

        /// <inheritdoc cref="INumberBase{TSelf}.Abs(TSelf)" />
        public static NFloat Abs(NFloat value) =>
            IntPtr.Size == 8
                ? new NFloat(double.Abs(value.Value64))
                : new NFloat(float.Abs(value.Value32));

        /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NFloat CreateChecked<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            NFloat result;

            if (typeof(TOther) == typeof(NFloat))
            {
                result = (NFloat)(object)value;
            }
            else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToChecked(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NFloat CreateSaturating<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            NFloat result;

            if (typeof(TOther) == typeof(NFloat))
            {
                result = (NFloat)(object)value;
            }
            else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToSaturating(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NFloat CreateTruncating<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            NFloat result;

            if (typeof(TOther) == typeof(NFloat))
            {
                result = (NFloat)(object)value;
            }
            else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToTruncating(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.IsCanonical(TSelf)" />
        static bool INumberBase<NFloat>.IsCanonical(NFloat value) => true;

        /// <inheritdoc cref="INumberBase{TSelf}.IsComplexNumber(TSelf)" />
        static bool INumberBase<NFloat>.IsComplexNumber(NFloat value) => false;

        /// <inheritdoc cref="INumberBase{TSelf}.IsEvenInteger(TSelf)" />
        public static bool IsEvenInteger(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsEvenInteger(value.Value64)
                : float.IsEvenInteger(value.Value32);

        /// <inheritdoc cref="INumberBase{TSelf}.IsImaginaryNumber(TSelf)" />
        static bool INumberBase<NFloat>.IsImaginaryNumber(NFloat value) => false;

        /// <inheritdoc cref="INumberBase{TSelf}.IsInteger(TSelf)" />
        public static bool IsInteger(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsInteger(value.Value64)
                : float.IsInteger(value.Value32);

        /// <inheritdoc cref="INumberBase{TSelf}.IsOddInteger(TSelf)" />
        public static bool IsOddInteger(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsOddInteger(value.Value64)
                : float.IsOddInteger(value.Value32);

        /// <inheritdoc cref="INumberBase{TSelf}.IsPositive(TSelf)" />
        public static bool IsPositive(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsPositive(value.Value64)
                : float.IsPositive(value.Value32);

        /// <inheritdoc cref="INumberBase{TSelf}.IsRealNumber(TSelf)" />
        public static bool IsRealNumber(NFloat value) =>
            IntPtr.Size == 8
                ? double.IsRealNumber(value.Value64)
                : float.IsRealNumber(value.Value32);

        /// <inheritdoc cref="INumberBase{TSelf}.IsZero(TSelf)" />
        static bool INumberBase<NFloat>.IsZero(NFloat value) => (value == 0);

        /// <inheritdoc cref="INumberBase{TSelf}.MaxMagnitude(TSelf, TSelf)" />
        public static NFloat MaxMagnitude(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MaxMagnitude(x.Value64, y.Value64))
                : new NFloat(float.MaxMagnitude(x.Value32, y.Value32));

        /// <inheritdoc cref="INumberBase{TSelf}.MaxMagnitudeNumber(TSelf, TSelf)" />
        public static NFloat MaxMagnitudeNumber(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MaxMagnitudeNumber(x.Value64, y.Value64))
                : new NFloat(float.MaxMagnitudeNumber(x.Value32, y.Value32));

        /// <inheritdoc cref="INumberBase{TSelf}.MinMagnitude(TSelf, TSelf)" />
        public static NFloat MinMagnitude(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MinMagnitude(x.Value64, y.Value64))
                : new NFloat(float.MinMagnitude(x.Value32, y.Value32));

        /// <inheritdoc cref="INumberBase{TSelf}.MinMagnitudeNumber(TSelf, TSelf)" />
        public static NFloat MinMagnitudeNumber(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.MinMagnitudeNumber(x.Value64, y.Value64))
                : new NFloat(float.MinMagnitudeNumber(x.Value32, y.Value32));

        /// <inheritdoc cref="INumberBase{TSelf}.MultiplyAddEstimate(TSelf, TSelf, TSelf)" />
        public static NFloat MultiplyAddEstimate(NFloat left, NFloat right, NFloat addend) =>
            IntPtr.Size == 8
                ? new NFloat(double.MultiplyAddEstimate(left.Value64, right.Value64, addend.Value64))
                : new NFloat(float.MultiplyAddEstimate(left.Value32, right.Value32, addend.Value32));

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<NFloat>.TryConvertFromChecked<TOther>(TOther value, out NFloat result)
        {
            return TryConvertFrom(value, out result);
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromSaturating{TOther}(TOther, out TSelf)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<NFloat>.TryConvertFromSaturating<TOther>(TOther value, out NFloat result)
        {
            return TryConvertFrom(value, out result);
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromTruncating{TOther}(TOther, out TSelf)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<NFloat>.TryConvertFromTruncating<TOther>(TOther value, out NFloat result)
        {
            return TryConvertFrom(value, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertFrom<TOther>(TOther value, out NFloat result)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte))
            {
                byte actualValue = (byte)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(char))
            {
                char actualValue = (char)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(decimal))
            {
                decimal actualValue = (decimal)(object)value;
                result = (NFloat)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(double))
            {
                double actualValue = (double)(object)value;
                result = (NFloat)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(Half))
            {
                Half actualValue = (Half)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(short))
            {
                short actualValue = (short)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(int))
            {
                int actualValue = (int)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(long))
            {
                long actualValue = (long)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(Int128))
            {
                Int128 actualValue = (Int128)(object)value;
                result = (NFloat)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(nint))
            {
                nint actualValue = (nint)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(sbyte))
            {
                sbyte actualValue = (sbyte)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(float))
            {
                float actualValue = (float)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ushort))
            {
                ushort actualValue = (ushort)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(uint))
            {
                uint actualValue = (uint)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ulong))
            {
                ulong actualValue = (ulong)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(UInt128))
            {
                UInt128 actualValue = (UInt128)(object)value;
                result = (NFloat)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(nuint))
            {
                nuint actualValue = (nuint)(object)value;
                result = actualValue;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToChecked{TOther}(TSelf, out TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<NFloat>.TryConvertToChecked<TOther>(NFloat value, [MaybeNullWhen(false)] out TOther result)
        {
            if (typeof(TOther) == typeof(byte))
            {
                byte actualResult = checked((byte)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(char))
            {
                char actualResult = checked((char)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(decimal))
            {
                decimal actualResult = checked((decimal)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(double))
            {
                double actualResult = value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Half))
            {
                Half actualResult = (Half)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(short))
            {
                short actualResult = checked((short)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(int))
            {
                int actualResult = checked((int)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(long))
            {
                long actualResult = checked((long)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Int128))
            {
                Int128 actualResult = checked((Int128)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(nint))
            {
                nint actualResult = checked((nint)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(sbyte))
            {
                sbyte actualResult = checked((sbyte)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(float))
            {
                float actualResult = (float)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(ushort))
            {
                ushort actualResult = checked((ushort)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(uint))
            {
                uint actualResult = checked((uint)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(ulong))
            {
                ulong actualResult = checked((ulong)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(UInt128))
            {
                UInt128 actualResult = checked((UInt128)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(nuint))
            {
                nuint actualResult = checked((nuint)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToSaturating{TOther}(TSelf, out TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<NFloat>.TryConvertToSaturating<TOther>(NFloat value, [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertTo(value, out result);
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToTruncating{TOther}(TSelf, out TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<NFloat>.TryConvertToTruncating<TOther>(NFloat value, [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertTo(value, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertTo<TOther>(NFloat value, [MaybeNullWhen(false)] out TOther result)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte))
            {
                byte actualResult = (value >= byte.MaxValue) ? byte.MaxValue :
                                    (value <= byte.MinValue) ? byte.MinValue : (byte)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(char))
            {
                char actualResult = (value >= char.MaxValue) ? char.MaxValue :
                                    (value <= char.MinValue) ? char.MinValue : (char)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(decimal))
            {
                decimal actualResult = (value >= +79228162514264337593543950336.0f) ? decimal.MaxValue :
                                       (value <= -79228162514264337593543950336.0f) ? decimal.MinValue :
                                       IsNaN(value) ? 0.0m : (decimal)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(double))
            {
                double actualResult = value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Half))
            {
                Half actualResult = (Half)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(short))
            {
                short actualResult = (value >= short.MaxValue) ? short.MaxValue :
                                     (value <= short.MinValue) ? short.MinValue : (short)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(int))
            {
                int actualResult = (value >= int.MaxValue) ? int.MaxValue :
                                   (value <= int.MinValue) ? int.MinValue : (int)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(long))
            {
                long actualResult = (value >= long.MaxValue) ? long.MaxValue :
                                    (value <= long.MinValue) ? long.MinValue : (long)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Int128))
            {
                Int128 actualResult = (value >= +170141183460469231731687303715884105727.0) ? Int128.MaxValue :
                                      (value <= -170141183460469231731687303715884105728.0) ? Int128.MinValue : (Int128)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(nint))
            {
                nint actualResult = (value >= nint.MaxValue) ? nint.MaxValue :
                                    (value <= nint.MinValue) ? nint.MinValue : (nint)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(sbyte))
            {
                sbyte actualResult = (value >= sbyte.MaxValue) ? sbyte.MaxValue :
                                     (value <= sbyte.MinValue) ? sbyte.MinValue : (sbyte)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(float))
            {
                float actualResult = (float)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(ushort))
            {
                ushort actualResult = (value >= ushort.MaxValue) ? ushort.MaxValue :
                                      (value <= ushort.MinValue) ? ushort.MinValue : (ushort)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(uint))
            {
                uint actualResult = (value >= uint.MaxValue) ? uint.MaxValue :
                                    (value <= uint.MinValue) ? uint.MinValue : (uint)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(ulong))
            {
                ulong actualResult = (value >= ulong.MaxValue) ? ulong.MaxValue :
                                     (value <= ulong.MinValue) ? ulong.MinValue :
                                     IsNaN(value) ? 0 : (ulong)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(UInt128))
            {
                UInt128 actualResult = (value >= 340282366920938463463374607431768211455.0) ? UInt128.MaxValue :
                                       (value <= 0.0) ? UInt128.MinValue : (UInt128)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(nuint))
            {
                nuint actualResult;
                if (IntPtr.Size == 8)
                {
                    actualResult = (value >= ulong.MaxValue) ? unchecked((nuint)ulong.MaxValue) :
                                   (value <= ulong.MinValue) ? unchecked((nuint)ulong.MinValue) : (nuint)value;
                }
                else
                {
                    actualResult = (value >= uint.MaxValue) ? unchecked((nuint)uint.MaxValue) :
                                   (value <= uint.MinValue) ? unchecked((nuint)uint.MinValue) : (nuint)value;
                }
                result = (TOther)(object)actualResult;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryParsePartial(string, NumberStyles, IFormatProvider?, out TSelf, out int)" />
        public static bool TryParsePartial([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out NFloat result, out int charsConsumed)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
                return Number.TryParseFloat(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out Unsafe.As<NFloat, double>(ref result), out charsConsumed);
            }
            else
            {
                Unsafe.SkipInit(out result);
                NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
                return Number.TryParseFloat(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out Unsafe.As<NFloat, float>(ref result), out charsConsumed);
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryParsePartial(ReadOnlySpan{char}, NumberStyles, IFormatProvider?, out TSelf, out int)" />
        public static bool TryParsePartial(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out NFloat result, out int charsConsumed)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
                return Number.TryParseFloat(s, style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out Unsafe.As<NFloat, double>(ref result), out charsConsumed);
            }
            else
            {
                Unsafe.SkipInit(out result);
                NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
                return Number.TryParseFloat(s, style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out Unsafe.As<NFloat, float>(ref result), out charsConsumed);
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryParsePartial(ReadOnlySpan{byte}, NumberStyles, IFormatProvider?, out TSelf, out int)" />
        public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out NFloat result, out int bytesConsumed)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
                return Number.TryParseFloat(utf8Text, style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out Unsafe.As<NFloat, double>(ref result), out bytesConsumed);
            }
            else
            {
                Unsafe.SkipInit(out result);
                NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
                return Number.TryParseFloat(utf8Text, style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out Unsafe.As<NFloat, float>(ref result), out bytesConsumed);
            }
        }

        //
        // IParsable
        //

        /// <inheritdoc cref="IParsable{TSelf}.TryParse(string?, IFormatProvider?, out TSelf)" />
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out NFloat result) => TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);

        //
        // IPowerFunctions
        //

        /// <inheritdoc cref="IPowerFunctions{TSelf}.Pow(TSelf, TSelf)" />
        public static NFloat Pow(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.Pow(x.Value64, y.Value64))
                : new NFloat(float.Pow(x.Value32, y.Value32));

        //
        // IRootFunctions
        //

        /// <inheritdoc cref="IRootFunctions{TSelf}.Cbrt(TSelf)" />
        public static NFloat Cbrt(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Cbrt(x.Value64))
                : new NFloat(float.Cbrt(x.Value32));

        /// <inheritdoc cref="IRootFunctions{TSelf}.Hypot(TSelf, TSelf)" />
        public static NFloat Hypot(NFloat x, NFloat y) =>
            IntPtr.Size == 8
                ? new NFloat(double.Hypot(x.Value64, y.Value64))
                : new NFloat(float.Hypot(x.Value32, y.Value32));

        /// <inheritdoc cref="IRootFunctions{TSelf}.RootN(TSelf, int)" />
        public static NFloat RootN(NFloat x, int n) =>
            IntPtr.Size == 8
                ? new NFloat(double.RootN(x.Value64, n))
                : new NFloat(float.RootN(x.Value32, n));

        /// <inheritdoc cref="IRootFunctions{TSelf}.Sqrt(TSelf)" />
        public static NFloat Sqrt(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Sqrt(x.Value64))
                : new NFloat(float.Sqrt(x.Value32));

        //
        // ISignedNumber
        //

        /// <inheritdoc cref="ISignedNumber{TSelf}.NegativeOne" />
        static NFloat ISignedNumber<NFloat>.NegativeOne =>
            IntPtr.Size == 8
                ? new NFloat(-1.0)
                : new NFloat(-1.0f);

        //
        // ISpanParsable
        //

        /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)" />
        public static NFloat Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider);

        /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse(ReadOnlySpan{char}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out NFloat result) => TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);

        //
        // ITrigonometricFunctions
        //

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.Acos(TSelf)" />
        public static NFloat Acos(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Acos(x.Value64))
                : new NFloat(float.Acos(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.AcosPi(TSelf)" />
        public static NFloat AcosPi(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.AcosPi(x.Value64))
                : new NFloat(float.AcosPi(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.Asin(TSelf)" />
        public static NFloat Asin(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Asin(x.Value64))
                : new NFloat(float.Asin(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.AsinPi(TSelf)" />
        public static NFloat AsinPi(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.AsinPi(x.Value64))
                : new NFloat(float.AsinPi(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.Atan(TSelf)" />
        public static NFloat Atan(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Atan(x.Value64))
                : new NFloat(float.Atan(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.AtanPi(TSelf)" />
        public static NFloat AtanPi(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.AtanPi(x.Value64))
                : new NFloat(float.AtanPi(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.Cos(TSelf)" />
        public static NFloat Cos(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Cos(x.Value64))
                : new NFloat(float.Cos(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.CosPi(TSelf)" />
        public static NFloat CosPi(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.CosPi(x.Value64))
                : new NFloat(float.CosPi(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.DegreesToRadians(TSelf)" />
        public static NFloat DegreesToRadians(NFloat degrees)
        {
            if (IntPtr.Size == 8)
            {
                // NOTE: Don't change the algorithm without consulting the DIM
                // which elaborates on why this implementation was chosen

                return new NFloat(double.DegreesToRadians(degrees.Value64));
            }
            else
            {
                // NOTE: Don't change the algorithm without consulting the DIM
                // which elaborates on why this implementation was chosen

                return new NFloat(float.DegreesToRadians(degrees.Value32));
            }
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.RadiansToDegrees(TSelf)" />
        public static NFloat RadiansToDegrees(NFloat radians)
        {
            if (IntPtr.Size == 8)
            {
                // NOTE: Don't change the algorithm without consulting the DIM
                // which elaborates on why this implementation was chosen

                return new NFloat(double.RadiansToDegrees(radians.Value64));
            }
            else
            {
                // NOTE: Don't change the algorithm without consulting the DIM
                // which elaborates on why this implementation was chosen

                return new NFloat(float.RadiansToDegrees(radians.Value32));
            }
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.Sin(TSelf)" />
        public static NFloat Sin(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Sin(x.Value64))
                : new NFloat(float.Sin(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.SinCos(TSelf)" />
        public static (NFloat Sin, NFloat Cos) SinCos(NFloat x)
        {
            if (IntPtr.Size == 8)
            {
                var (sin, cos) = double.SinCos(x.Value64);
                return (new NFloat(sin), new NFloat(cos));
            }
            else
            {
                var (sin, cos) = float.SinCos(x.Value32);
                return (new NFloat(sin), new NFloat(cos));
            }
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.SinCosPi(TSelf)" />
        public static (NFloat SinPi, NFloat CosPi) SinCosPi(NFloat x)
        {
            if (IntPtr.Size == 8)
            {
                var (sinPi, cosPi) = double.SinCosPi(x.Value64);
                return (new NFloat(sinPi), new NFloat(cosPi));
            }
            else
            {
                var (sinPi, cosPi) = float.SinCosPi(x.Value32);
                return (new NFloat(sinPi), new NFloat(cosPi));
            }
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.SinPi(TSelf)" />
        public static NFloat SinPi(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.SinPi(x.Value64))
                : new NFloat(float.SinPi(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.Tan(TSelf)" />
        public static NFloat Tan(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.Tan(x.Value64))
                : new NFloat(float.Tan(x.Value32));

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.TanPi(TSelf)" />
        public static NFloat TanPi(NFloat x) =>
            IntPtr.Size == 8
                ? new NFloat(double.TanPi(x.Value64))
                : new NFloat(float.TanPi(x.Value32));

        //
        // IUtf8SpanParsable
        //

        /// <inheritdoc cref="INumberBase{TSelf}.Parse(ReadOnlySpan{byte}, NumberStyles, IFormatProvider?)" />
        public static NFloat Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = NumberStyles.Float | NumberStyles.AllowThousands, IFormatProvider? provider = null)
        {
            if (IntPtr.Size == 8)
            {
                var result = double.Parse(utf8Text, style, provider);
                return new NFloat(result);
            }
            else
            {
                var result = float.Parse(utf8Text, style, provider);
                return new NFloat(result);
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryParse(ReadOnlySpan{byte}, NumberStyles, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out NFloat result)
        {
            if (IntPtr.Size == 8)
            {
                Unsafe.SkipInit(out result);
                return double.TryParse(utf8Text, style, provider, out Unsafe.As<NFloat, double>(ref result));
            }
            else
            {
                Unsafe.SkipInit(out result);
                return float.TryParse(utf8Text, style, provider, out Unsafe.As<NFloat, float>(ref result));
            }
        }

        /// <inheritdoc cref="IUtf8SpanParsable{TSelf}.Parse(ReadOnlySpan{byte}, IFormatProvider?)" />
        public static NFloat Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, NumberStyles.Float | NumberStyles.AllowThousands, provider);

        /// <inheritdoc cref="IUtf8SpanParsable{TSelf}.TryParse(ReadOnlySpan{byte}, IFormatProvider?, out TSelf)" />
        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out NFloat result) => TryParse(utf8Text, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
    }
}
