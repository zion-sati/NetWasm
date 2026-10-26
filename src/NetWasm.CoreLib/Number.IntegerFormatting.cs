// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Integer decimal-string methods and their dependencies copied from
// dotnet/runtime Number.Formatting.cs at
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Runtime allocation is supplied by NetWasm's String.FastAllocateString.

using System.Buffers.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Numerics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    internal static partial class Number
    {
        // Shared integer precision constants from upstream Number.Parsing.cs.
        private const int Int32Precision = 10;
        private const int UInt32Precision = Int32Precision;
        private const int Int64Precision = 19;
        private const int UInt64Precision = 20;
        private const int Int128Precision = 39;
        private const int UInt128Precision = 39;
        /// <summary>The non-inclusive upper bound of <see cref="s_smallNumberCache"/>.</summary>
        /// <remarks>
        /// This is a semi-arbitrary bound. For mono, which is often used for more size-constrained workloads,
        /// we keep the size really small, supporting only single digit values.  For coreclr, we use a larger
        /// value, still relatively small but large enough to accommodate common sources of numbers to strings, e.g. HTTP success status codes.
        /// By being >= 255, it also accommodates all byte.ToString()s.  If no small numbers are ever formatted, we incur
        /// the ~2400 bytes on 64-bit for the array itself.  If all small numbers are formatted, we incur ~11,500 bytes
        /// on 64-bit for the array and all the strings.
        /// </remarks>
        private const int SmallNumberCacheLength =
#if MONO
            10;
#else
            300;
#endif
        /// <summary>Lazily-populated cache of strings for uint values in the range [0, <see cref="SmallNumberCacheLength"/>).</summary>
        private static readonly string?[] s_smallNumberCache = new string[SmallNumberCacheLength];

        // Optimizations using "TwoDigits" inspired by:
        // https://engineering.fb.com/2013/03/15/developer-tools/three-optimization-tips-for-c/
#if MONO
        // Workaround for a performance regression on Mono: https://github.com/dotnet/runtime/issues/111932
        private static readonly byte[] TwoDigitsCharsAsBytes =
            MemoryMarshal.AsBytes<char>("00010203040506070809" +
                                        "10111213141516171819" +
                                        "20212223242526272829" +
                                        "30313233343536373839" +
                                        "40414243444546474849" +
                                        "50515253545556575859" +
                                        "60616263646566676869" +
                                        "70717273747576777879" +
                                        "80818283848586878889" +
                                        "90919293949596979899").ToArray();
        private static readonly byte[] TwoDigitsBytes =
                                       ("00010203040506070809"u8 +
                                        "10111213141516171819"u8 +
                                        "20212223242526272829"u8 +
                                        "30313233343536373839"u8 +
                                        "40414243444546474849"u8 +
                                        "50515253545556575859"u8 +
                                        "60616263646566676869"u8 +
                                        "70717273747576777879"u8 +
                                        "80818283848586878889"u8 +
                                        "90919293949596979899"u8).ToArray();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte GetTwoDigitsBytesRef(bool useChars) =>
            ref MemoryMarshal.GetArrayDataReference(useChars ? TwoDigitsCharsAsBytes : TwoDigitsBytes);
#else
        // NetWasm keeps the char table as a constant string and refers to its
        // characters directly: building ReadOnlySpan temporaries to reach them
        // gives every lookup a frame-arena allocation and a zeroing call.
        private const string TwoDigitsChars =
            "00010203040506070809" +
            "10111213141516171819" +
            "20212223242526272829" +
            "30313233343536373839" +
            "40414243444546474849" +
            "50515253545556575859" +
            "60616263646566676869" +
            "70717273747576777879" +
            "80818283848586878889" +
            "90919293949596979899";
        private static ReadOnlySpan<byte> TwoDigitsBytes =>
                                        "00010203040506070809"u8 +
                                        "10111213141516171819"u8 +
                                        "20212223242526272829"u8 +
                                        "30313233343536373839"u8 +
                                        "40414243444546474849"u8 +
                                        "50515253545556575859"u8 +
                                        "60616263646566676869"u8 +
                                        "70717273747576777879"u8 +
                                        "80818283848586878889"u8 +
                                        "90919293949596979899"u8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref byte GetTwoDigitsBytesRef(bool useChars) =>
            ref useChars
                ? ref Unsafe.As<char, byte>(ref TwoDigitsChars.GetRawStringData())
                : ref MemoryMarshal.GetReference(TwoDigitsBytes);
#endif

        internal static char GetHexBase(char fmt)
        {
            // The fmt-(X-A+10) hack has the effect of dictating whether we produce uppercase or lowercase
            // hex numbers for a-f. 'X' as the fmt code produces uppercase. 'x' as the format code produces lowercase.
            return (char)(fmt - ('X' - 'A' + 10));
        }

        public static string FormatInt32(int value, int hexMask, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return value >= 0 ?
                    UInt32ToDecStr((uint)value) :
                    NegativeInt32ToDecStr(value, digits: -1, NumberFormatInfo.GetInstance(provider).NegativeSign);
            }

            return FormatInt32Slow(value, hexMask, format, provider);

            static unsafe string FormatInt32Slow(int value, int hexMask, string? format, IFormatProvider? provider)
            {
                ReadOnlySpan<char> formatSpan = format;
                char fmt = ParseFormatSpecifier(formatSpan, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return value >= 0 ?
                        UInt32ToDecStr((uint)value, digits) :
                        NegativeInt32ToDecStr(value, digits, NumberFormatInfo.GetInstance(provider).NegativeSign);
                }
                else if (fmtUpper == 'X')
                {
                    return Int32ToHexStr(value & hexMask, GetHexBase(fmt), digits);
                }
                else if (fmtUpper == 'B')
                {
                    return UInt32ToBinaryStr((uint)(value & hexMask), digits);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[Int32NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, Int32NumberBufferLength);

                    Int32ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, formatSpan, info);
                    }

                    string result = vlb.AsSpan().ToString();
                    vlb.Dispose();
                    return result;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // expose to caller's likely-const format to trim away slow path
        public static bool TryFormatInt32<TChar>(int value, int hexMask, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            // Fast path for default format
            if (format.Length == 0)
            {
                return value >= 0 ?
                    TryUInt32ToDecStr((uint)value, destination, out charsWritten) :
                    TryNegativeInt32ToDecStr(value, digits: -1, NumberFormatInfo.GetInstance(provider).NegativeSignTChar<TChar>(), destination, out charsWritten);
            }

            return TryFormatInt32Slow(value, hexMask, format, provider, destination, out charsWritten);

            static unsafe bool TryFormatInt32Slow(int value, int hexMask, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten)
            {
                char fmt = ParseFormatSpecifier(format, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return value >= 0 ?
                        TryUInt32ToDecStr((uint)value, digits, destination, out charsWritten) :
                        TryNegativeInt32ToDecStr(value, digits, NumberFormatInfo.GetInstance(provider).NegativeSignTChar<TChar>(), destination, out charsWritten);
                }
                else if (fmtUpper == 'X')
                {
                    return TryInt32ToHexStr(value & hexMask, GetHexBase(fmt), digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'B')
                {
                    return TryUInt32ToBinaryStr((uint)(value & hexMask), digits, destination, out charsWritten);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[Int32NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, Int32NumberBufferLength);

                    Int32ToNumber(value, ref number);

                    TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
                    var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, format, info);
                    }

                    bool success = vlb.TryCopyTo(destination, out charsWritten);
                    vlb.Dispose();
                    return success;
                }
            }
        }

        public static string FormatUInt32(uint value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt32ToDecStr(value);
            }

            return FormatUInt32Slow(value, format, provider);

            static unsafe string FormatUInt32Slow(uint value, string? format, IFormatProvider? provider)
            {
                ReadOnlySpan<char> formatSpan = format;
                char fmt = ParseFormatSpecifier(formatSpan, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return UInt32ToDecStr(value, digits);
                }
                else if (fmtUpper == 'X')
                {
                    return Int32ToHexStr((int)value, GetHexBase(fmt), digits);
                }
                else if (fmtUpper == 'B')
                {
                    return UInt32ToBinaryStr(value, digits);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[UInt32NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, UInt32NumberBufferLength);

                    UInt32ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, formatSpan, info);
                    }

                    string result = vlb.AsSpan().ToString();
                    vlb.Dispose();
                    return result;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // expose to caller's likely-const format to trim away slow path
        public static bool TryFormatUInt32<TChar>(uint value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            // Fast path for default format
            if (format.Length == 0)
            {
                return TryUInt32ToDecStr(value, destination, out charsWritten);
            }

            return TryFormatUInt32Slow(value, format, provider, destination, out charsWritten);

            static unsafe bool TryFormatUInt32Slow(uint value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten)
            {
                char fmt = ParseFormatSpecifier(format, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return TryUInt32ToDecStr(value, digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'X')
                {
                    return TryInt32ToHexStr((int)value, GetHexBase(fmt), digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'B')
                {
                    return TryUInt32ToBinaryStr(value, digits, destination, out charsWritten);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[UInt32NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, UInt32NumberBufferLength);

                    UInt32ToNumber(value, ref number);

                    TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
                    var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, format, info);
                    }

                    bool success = vlb.TryCopyTo(destination, out charsWritten);
                    vlb.Dispose();
                    return success;
                }
            }
        }

        public static string FormatInt64(long value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return value >= 0 ?
                    UInt64ToDecStr((ulong)value) :
                    NegativeInt64ToDecStr(value, digits: -1, NumberFormatInfo.GetInstance(provider).NegativeSign);
            }

            return FormatInt64Slow(value, format, provider);

            static unsafe string FormatInt64Slow(long value, string? format, IFormatProvider? provider)
            {
                ReadOnlySpan<char> formatSpan = format;
                char fmt = ParseFormatSpecifier(formatSpan, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return value >= 0 ?
                        UInt64ToDecStr((ulong)value, digits) :
                        NegativeInt64ToDecStr(value, digits, NumberFormatInfo.GetInstance(provider).NegativeSign);
                }
                else if (fmtUpper == 'X')
                {
                    return Int64ToHexStr(value, GetHexBase(fmt), digits);
                }
                else if (fmtUpper == 'B')
                {
                    return UInt64ToBinaryStr((ulong)value, digits);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[Int64NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, Int64NumberBufferLength);

                    Int64ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, formatSpan, info);
                    }

                    string result = vlb.AsSpan().ToString();
                    vlb.Dispose();
                    return result;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // expose to caller's likely-const format to trim away slow path
        public static bool TryFormatInt64<TChar>(long value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            // Fast path for default format
            if (format.Length == 0)
            {
                return value >= 0 ?
                    TryUInt64ToDecStr((ulong)value, destination, out charsWritten) :
                    TryNegativeInt64ToDecStr(value, digits: -1, NumberFormatInfo.GetInstance(provider).NegativeSignTChar<TChar>(), destination, out charsWritten);
            }

            return TryFormatInt64Slow(value, format, provider, destination, out charsWritten);

            static unsafe bool TryFormatInt64Slow(long value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten)
            {
                char fmt = ParseFormatSpecifier(format, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return value >= 0 ?
                        TryUInt64ToDecStr((ulong)value, digits, destination, out charsWritten) :
                        TryNegativeInt64ToDecStr(value, digits, NumberFormatInfo.GetInstance(provider).NegativeSignTChar<TChar>(), destination, out charsWritten);
                }
                else if (fmtUpper == 'X')
                {
                    return TryInt64ToHexStr(value, GetHexBase(fmt), digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'B')
                {
                    return TryUInt64ToBinaryStr((ulong)value, digits, destination, out charsWritten);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[Int64NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, Int64NumberBufferLength);

                    Int64ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, format, info);
                    }

                    bool success = vlb.TryCopyTo(destination, out charsWritten);
                    vlb.Dispose();
                    return success;
                }
            }
        }

        public static string FormatUInt64(ulong value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt64ToDecStr(value);
            }

            return FormatUInt64Slow(value, format, provider);

            static unsafe string FormatUInt64Slow(ulong value, string? format, IFormatProvider? provider)
            {
                ReadOnlySpan<char> formatSpan = format;
                char fmt = ParseFormatSpecifier(formatSpan, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return UInt64ToDecStr(value, digits);
                }
                else if (fmtUpper == 'X')
                {
                    return Int64ToHexStr((long)value, GetHexBase(fmt), digits);
                }
                else if (fmtUpper == 'B')
                {
                    return UInt64ToBinaryStr(value, digits);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[UInt64NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, UInt64NumberBufferLength);

                    UInt64ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, formatSpan, info);
                    }

                    string result = vlb.AsSpan().ToString();
                    vlb.Dispose();
                    return result;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // expose to caller's likely-const format to trim away slow path
        public static bool TryFormatUInt64<TChar>(ulong value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            // Fast path for default format
            if (format.Length == 0)
            {
                return TryUInt64ToDecStr(value, destination, out charsWritten);
            }

            return TryFormatUInt64Slow(value, format, provider, destination, out charsWritten);

            static unsafe bool TryFormatUInt64Slow(ulong value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten)
            {
                char fmt = ParseFormatSpecifier(format, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison
                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return TryUInt64ToDecStr(value, digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'X')
                {
                    return TryInt64ToHexStr((long)value, GetHexBase(fmt), digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'B')
                {
                    return TryUInt64ToBinaryStr(value, digits, destination, out charsWritten);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[UInt64NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, UInt64NumberBufferLength);

                    UInt64ToNumber(value, ref number);

                    TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
                    var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, format, info);
                    }

                    bool success = vlb.TryCopyTo(destination, out charsWritten);
                    vlb.Dispose();
                    return success;
                }
            }
        }

        public static string FormatInt128(Int128 value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return Int128.IsPositive(value)
                     ? UInt128ToDecStr((UInt128)value, digits: -1)
                     : NegativeInt128ToDecStr(value, digits: -1, NumberFormatInfo.GetInstance(provider).NegativeSign);
            }

            return FormatInt128Slow(value, format, provider);

            static unsafe string FormatInt128Slow(Int128 value, string? format, IFormatProvider? provider)
            {
                ReadOnlySpan<char> formatSpan = format;

                char fmt = ParseFormatSpecifier(formatSpan, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison

                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return Int128.IsPositive(value)
                        ? UInt128ToDecStr((UInt128)value, digits)
                        : NegativeInt128ToDecStr(value, digits, NumberFormatInfo.GetInstance(provider).NegativeSign);
                }
                else if (fmtUpper == 'X')
                {
                    return Int128ToHexStr(value, GetHexBase(fmt), digits);
                }
                else if (fmtUpper == 'B')
                {
                    return UInt128ToBinaryStr(value, digits);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[Int128NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, Int128NumberBufferLength);

                    Int128ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, formatSpan, info);
                    }

                    string result = vlb.AsSpan().ToString();
                    vlb.Dispose();
                    return result;
                }
            }
        }

        public static bool TryFormatInt128<TChar>(Int128 value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            // Fast path for default format
            if (format.Length == 0)
            {
                return Int128.IsPositive(value)
                     ? TryUInt128ToDecStr((UInt128)value, digits: -1, destination, out charsWritten)
                     : TryNegativeInt128ToDecStr(value, digits: -1, NumberFormatInfo.GetInstance(provider).NegativeSignTChar<TChar>(), destination, out charsWritten);
            }

            return TryFormatInt128Slow(value, format, provider, destination, out charsWritten);

            static unsafe bool TryFormatInt128Slow(Int128 value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten)
            {
                char fmt = ParseFormatSpecifier(format, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison

                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return Int128.IsPositive(value)
                        ? TryUInt128ToDecStr((UInt128)value, digits, destination, out charsWritten)
                        : TryNegativeInt128ToDecStr(value, digits, NumberFormatInfo.GetInstance(provider).NegativeSignTChar<TChar>(), destination, out charsWritten);
                }
                else if (fmtUpper == 'X')
                {
                    return TryInt128ToHexStr(value, GetHexBase(fmt), digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'B')
                {
                    return TryUInt128ToBinaryStr(value, digits, destination, out charsWritten);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[Int128NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, Int128NumberBufferLength);

                    Int128ToNumber(value, ref number);

                    TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
                    var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, format, info);
                    }

                    bool success = vlb.TryCopyTo(destination, out charsWritten);
                    vlb.Dispose();
                    return success;
                }
            }
        }

        public static string FormatUInt128(UInt128 value, string? format, IFormatProvider? provider)
        {
            // Fast path for default format
            if (string.IsNullOrEmpty(format))
            {
                return UInt128ToDecStr(value, digits: -1);
            }

            return FormatUInt128Slow(value, format, provider);

            static unsafe string FormatUInt128Slow(UInt128 value, string? format, IFormatProvider? provider)
            {
                ReadOnlySpan<char> formatSpan = format;

                char fmt = ParseFormatSpecifier(formatSpan, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison

                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return UInt128ToDecStr(value, digits);
                }
                else if (fmtUpper == 'X')
                {
                    return Int128ToHexStr((Int128)value, GetHexBase(fmt), digits);
                }
                else if (fmtUpper == 'B')
                {
                    return UInt128ToBinaryStr((Int128)value, digits);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[UInt128NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, UInt128NumberBufferLength);

                    UInt128ToNumber(value, ref number);

                    char* stackPtr = stackalloc char[CharStackBufferSize];
                    var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, formatSpan, info);
                    }

                    string result = vlb.AsSpan().ToString();
                    vlb.Dispose();
                    return result;
                }
            }
        }

        public static bool TryFormatUInt128<TChar>(UInt128 value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            // Fast path for default format
            if (format.Length == 0)
            {
                return TryUInt128ToDecStr(value, digits: -1, destination, out charsWritten);
            }

            return TryFormatUInt128Slow(value, format, provider, destination, out charsWritten);

            static unsafe bool TryFormatUInt128Slow(UInt128 value, ReadOnlySpan<char> format, IFormatProvider? provider, Span<TChar> destination, out int charsWritten)
            {
                char fmt = ParseFormatSpecifier(format, out int digits);
                char fmtUpper = (char)(fmt & 0xFFDF); // ensure fmt is upper-cased for purposes of comparison

                if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
                {
                    return TryUInt128ToDecStr(value, digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'X')
                {
                    return TryInt128ToHexStr((Int128)value, GetHexBase(fmt), digits, destination, out charsWritten);
                }
                else if (fmtUpper == 'B')
                {
                    return TryUInt128ToBinaryStr((Int128)value, digits, destination, out charsWritten);
                }
                else
                {
                    NumberFormatInfo info = NumberFormatInfo.GetInstance(provider);

                    byte* pDigits = stackalloc byte[UInt128NumberBufferLength];
                    NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, pDigits, UInt128NumberBufferLength);

                    UInt128ToNumber(value, ref number);

                    TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
                    var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));

                    if (fmt != 0)
                    {
                        NumberToString(ref vlb, ref number, fmt, digits, info);
                    }
                    else
                    {
                        NumberToStringFormat(ref vlb, ref number, format, info);
                    }

                    bool success = vlb.TryCopyTo(destination, out charsWritten);
                    vlb.Dispose();
                    return success;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Int32ToNumber(int value, ref NumberBuffer number)
        {
            number.DigitsCount = Int32Precision;

            if (value >= 0)
            {
                number.IsNegative = false;
            }
            else
            {
                number.IsNegative = true;
                value = -value;
            }

            byte* buffer = number.DigitsPtr;
            byte* p = UInt32ToDecChars(buffer + Int32Precision, (uint)value, 0);

            int i = (int)(buffer + Int32Precision - p);

            number.DigitsCount = i;
            number.Scale = i;

            byte* dst = number.DigitsPtr;
            while (--i >= 0)
            {
                *dst++ = *p++;
            }
            *dst = (byte)'\0';

            number.CheckConsistency();
        }

        public static string Int32ToDecStr(int value) =>
            value >= 0 ?
                UInt32ToDecStr((uint)value) :
                NegativeInt32ToDecStr(value, -1, NumberFormatInfo.CurrentNegativeSign);

        private static unsafe string NegativeInt32ToDecStr(int value, int digits, string sNegative)
        {
            Debug.Assert(value < 0);

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits((uint)(-value))) + sNegative.Length;
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = UInt32ToDecChars(buffer + bufferLength, (uint)(-value), digits);
                Debug.Assert(p == buffer + sNegative.Length);

                for (int i = sNegative.Length - 1; i >= 0; i--)
                {
                    *(--p) = sNegative[i];
                }
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe bool TryNegativeInt32ToDecStr<TChar>(int value, int digits, ReadOnlySpan<TChar> sNegative, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            Debug.Assert(value < 0);

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits((uint)(-value))) + sNegative.Length;
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = UInt32ToDecChars(buffer + bufferLength, (uint)(-value), digits);
                Debug.Assert(p == buffer + sNegative.Length);

                for (int i = sNegative.Length - 1; i >= 0; i--)
                {
                    *(--p) = sNegative[i];
                }
                Debug.Assert(p == buffer);
            }
            return true;
        }

        private static unsafe string Int32ToHexStr(int value, char hexBase, int digits)
        {
            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountHexDigits((uint)value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = Int32ToHexChars(buffer + bufferLength, (uint)value, hexBase, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe bool TryInt32ToHexStr<TChar>(int value, char hexBase, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountHexDigits((uint)value));
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = Int32ToHexChars(buffer + bufferLength, (uint)value, hexBase, digits);
                Debug.Assert(p == buffer);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe TChar* Int32ToHexChars<TChar>(TChar* buffer, uint value, int hexBase, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            while (--digits >= 0 || value != 0)
            {
                byte digit = (byte)(value & 0xF);
                *(--buffer) = TChar.CastFrom(digit + (digit < 10 ? (byte)'0' : hexBase));
                value >>= 4;
            }
            return buffer;
        }

        private static unsafe string UInt32ToBinaryStr(uint value, int digits)
        {
            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, 32 - (int)uint.LeadingZeroCount(value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = UInt32ToBinaryChars(buffer + bufferLength, value, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe bool TryUInt32ToBinaryStr<TChar>(uint value, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, 32 - (int)uint.LeadingZeroCount(value));
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = UInt32ToBinaryChars(buffer + bufferLength, value, digits);
                Debug.Assert(p == buffer);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe TChar* UInt32ToBinaryChars<TChar>(TChar* buffer, uint value, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            while (--digits >= 0 || value != 0)
            {
                *(--buffer) = TChar.CastFrom('0' + (byte)(value & 0x1));
                value >>= 1;
            }
            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void UInt32ToNumber(uint value, ref NumberBuffer number)
        {
            number.DigitsCount = UInt32Precision;
            number.IsNegative = false;

            byte* buffer = number.DigitsPtr;
            byte* p = UInt32ToDecChars(buffer + UInt32Precision, value, 0);

            int i = (int)(buffer + UInt32Precision - p);

            number.DigitsCount = i;
            number.Scale = i;

            byte* dst = number.DigitsPtr;
            while (--i >= 0)
            {
                *dst++ = *p++;
            }
            *dst = (byte)'\0';

            number.CheckConsistency();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void WriteTwoDigits<TChar>(uint value, TChar* ptr) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            Debug.Assert(value <= 99);

            Unsafe.CopyBlockUnaligned(
                ref *(byte*)ptr,
                ref Unsafe.Add(ref GetTwoDigitsBytesRef(typeof(TChar) == typeof(char)), (uint)sizeof(TChar) * 2 * value),
                (uint)sizeof(TChar) * 2);
        }

        /// <summary>
        /// Writes a value [ 0000 .. 9999 ] to the buffer starting at the specified offset.
        /// This method performs best when the starting index is a constant literal.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void WriteFourDigits<TChar>(uint value, TChar* ptr) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            Debug.Assert(value <= 9999);

            (value, uint remainder) = Math.DivRem(value, 100);

            ref byte charsArray = ref GetTwoDigitsBytesRef(typeof(TChar) == typeof(char));

            Unsafe.CopyBlockUnaligned(
                ref *(byte*)ptr,
                ref Unsafe.Add(ref charsArray, (uint)sizeof(TChar) * 2 * value),
                (uint)sizeof(TChar) * 2);

            Unsafe.CopyBlockUnaligned(
                ref *(byte*)(ptr + 2),
                ref Unsafe.Add(ref charsArray, (uint)sizeof(TChar) * 2 * remainder),
                (uint)sizeof(TChar) * 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void WriteDigits<TChar>(uint value, TChar* ptr, int count) where TChar : unmanaged, IUtfChar<TChar>
        {
            TChar* cur;
            for (cur = ptr + count - 1; cur > ptr; cur--)
            {
                uint temp = '0' + value;
                value /= 10;
                *cur = TChar.CastFrom(temp - (value * 10));
            }

            Debug.Assert(value < 10);
            Debug.Assert(cur == ptr);
            *cur = TChar.CastFrom('0' + value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe TChar* UInt32ToDecChars<TChar>(TChar* bufferEnd, uint value) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (value >= 10)
            {
                // Handle all values >= 100 two-digits at a time so as to avoid expensive integer division operations.
                while (value >= 100)
                {
                    bufferEnd -= 2;
                    (value, uint remainder) = Math.DivRem(value, 100);
                    WriteTwoDigits(remainder, bufferEnd);
                }

                // If there are two digits remaining, store them.
                if (value >= 10)
                {
                    bufferEnd -= 2;
                    WriteTwoDigits(value, bufferEnd);
                    return bufferEnd;
                }
            }

            // Otherwise, store the single digit remaining.
            *(--bufferEnd) = TChar.CastFrom(value + '0');
            return bufferEnd;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe TChar* UInt32ToDecChars<TChar>(TChar* bufferEnd, uint value, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            uint remainder;
            while (value >= 100)
            {
                bufferEnd -= 2;
                digits -= 2;
                (value, remainder) = Math.DivRem(value, 100);
                WriteTwoDigits(remainder, bufferEnd);
            }

            while (value != 0 || digits > 0)
            {
                digits--;
                (value, remainder) = Math.DivRem(value, 10);
                *(--bufferEnd) = TChar.CastFrom(remainder + '0');
            }

            return bufferEnd;
        }

        internal static string UInt32ToDecStr(uint value)
        {
            // For small numbers, consult a lazily-populated cache.
            if (value < SmallNumberCacheLength)
            {
                return UInt32ToDecStrForKnownSmallNumber(value);
            }

            return UInt32ToDecStr_NoSmallNumberCheck(value);
        }

        internal static string UInt32ToDecStrForKnownSmallNumber(uint value)
        {
            Debug.Assert(value < SmallNumberCacheLength);
            return s_smallNumberCache[value] ?? CreateAndCacheString(value);

            [MethodImpl(MethodImplOptions.NoInlining)] // keep rare usage out of fast path
            static string CreateAndCacheString(uint value) =>
                s_smallNumberCache[value] = UInt32ToDecStr_NoSmallNumberCheck(value);
        }

        private static unsafe string UInt32ToDecStr_NoSmallNumberCheck(uint value)
        {
            int bufferLength = FormattingHelpers.CountDigits(value);

            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = buffer + bufferLength;
                p = UInt32ToDecChars(p, value);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe string UInt32ToDecStr(uint value, int digits)
        {
            if (digits <= 1)
                return UInt32ToDecStr(value);

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits(value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = buffer + bufferLength;
                p = UInt32ToDecChars(p, value, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe bool TryUInt32ToDecStr<TChar>(uint value, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            int bufferLength = FormattingHelpers.CountDigits(value);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
                {
                    TChar* p = UInt32ToDecChars(buffer + bufferLength, value);
                    Debug.Assert(p == buffer);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }

        internal static unsafe bool TryUInt32ToDecStr<TChar>(uint value, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            int countedDigits = FormattingHelpers.CountDigits(value);
            int bufferLength = Math.Max(digits, countedDigits);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
                {
                    TChar* p = buffer + bufferLength;
                    p = digits > countedDigits ?
                        UInt32ToDecChars(p, value, digits) :
                        UInt32ToDecChars(p, value);
                    Debug.Assert(p == buffer);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Int64ToNumber(long value, ref NumberBuffer number)
        {
            number.DigitsCount = Int64Precision;

            if (value >= 0)
            {
                number.IsNegative = false;
            }
            else
            {
                number.IsNegative = true;
                value = -value;
            }

            byte* buffer = number.DigitsPtr;
            byte* p = UInt64ToDecChars(buffer + Int64Precision, (ulong)value, 0);

            int i = (int)(buffer + Int64Precision - p);

            number.DigitsCount = i;
            number.Scale = i;

            byte* dst = number.DigitsPtr;
            while (--i >= 0)
            {
                *dst++ = *p++;
            }
            *dst = (byte)'\0';

            number.CheckConsistency();
        }

        public static string Int64ToDecStr(long value)
        {
            return value >= 0 ?
                UInt64ToDecStr((ulong)value) :
                NegativeInt64ToDecStr(value, -1, NumberFormatInfo.CurrentNegativeSign);
        }

        private static unsafe string NegativeInt64ToDecStr(long value, int digits, string sNegative)
        {
            Debug.Assert(value < 0);

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits((ulong)(-value))) + sNegative.Length;
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = UInt64ToDecChars(buffer + bufferLength, (ulong)(-value), digits);
                Debug.Assert(p == buffer + sNegative.Length);

                for (int i = sNegative.Length - 1; i >= 0; i--)
                {
                    *(--p) = sNegative[i];
                }
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe bool TryNegativeInt64ToDecStr<TChar>(long value, int digits, ReadOnlySpan<TChar> sNegative, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            Debug.Assert(value < 0);

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits((ulong)(-value))) + sNegative.Length;
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = UInt64ToDecChars(buffer + bufferLength, (ulong)(-value), digits);
                Debug.Assert(p == buffer + sNegative.Length);

                for (int i = sNegative.Length - 1; i >= 0; i--)
                {
                    *(--p) = sNegative[i];
                }
                Debug.Assert(p == buffer);
            }
            return true;
        }

        private static unsafe string Int64ToHexStr(long value, char hexBase, int digits)
        {
            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountHexDigits((ulong)value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = Int64ToHexChars(buffer + bufferLength, (ulong)value, hexBase, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe bool TryInt64ToHexStr<TChar>(long value, char hexBase, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountHexDigits((ulong)value));
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = Int64ToHexChars(buffer + bufferLength, (ulong)value, hexBase, digits);
                Debug.Assert(p == buffer);
            }
            return true;
        }

#if TARGET_64BIT
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static unsafe TChar* Int64ToHexChars<TChar>(TChar* buffer, ulong value, int hexBase, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
#if TARGET_32BIT
            uint lower = (uint)value;
            uint upper = (uint)(value >> 32);

            if (upper != 0)
            {
                buffer = Int32ToHexChars(buffer, lower, hexBase, 8);
                return Int32ToHexChars(buffer, upper, hexBase, digits - 8);
            }
            else
            {
                return Int32ToHexChars(buffer, lower, hexBase, Math.Max(digits, 1));
            }
#else
            while (--digits >= 0 || value != 0)
            {
                byte digit = (byte)(value & 0xF);
                *(--buffer) = TChar.CastFrom(digit + (digit < 10 ? (byte)'0' : hexBase));
                value >>= 4;
            }
            return buffer;
#endif
        }

        private static unsafe string UInt64ToBinaryStr(ulong value, int digits)
        {
            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, 64 - (int)ulong.LeadingZeroCount(value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = UInt64ToBinaryChars(buffer + bufferLength, value, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe bool TryUInt64ToBinaryStr<TChar>(ulong value, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (digits < 1)
            {
                digits = 1;
            }

            int bufferLength = Math.Max(digits, 64 - (int)ulong.LeadingZeroCount(value));
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = UInt64ToBinaryChars(buffer + bufferLength, value, digits);
                Debug.Assert(p == buffer);
            }
            return true;
        }

#if TARGET_64BIT
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private static unsafe TChar* UInt64ToBinaryChars<TChar>(TChar* buffer, ulong value, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
#if TARGET_32BIT
            uint lower = (uint)value;
            uint upper = (uint)(value >> 32);

            if (upper != 0)
            {
                buffer = UInt32ToBinaryChars(buffer, lower, 32);
                return UInt32ToBinaryChars(buffer, upper, digits - 32);
            }
            else
            {
                return UInt32ToBinaryChars(buffer, lower, Math.Max(digits, 1));
            }
#else
            while (--digits >= 0 || value != 0)
            {
                *(--buffer) = TChar.CastFrom('0' + (byte)(value & 0x1));
                value >>= 1;
            }
            return buffer;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void UInt64ToNumber(ulong value, ref NumberBuffer number)
        {
            number.DigitsCount = UInt64Precision;
            number.IsNegative = false;

            byte* buffer = number.DigitsPtr;
            byte* p = UInt64ToDecChars(buffer + UInt64Precision, value, 0);

            int i = (int)(buffer + UInt64Precision - p);

            number.DigitsCount = i;
            number.Scale = i;

            byte* dst = number.DigitsPtr;
            while (--i >= 0)
            {
                *dst++ = *p++;
            }
            *dst = (byte)'\0';

            number.CheckConsistency();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Int64DivMod1E9(ref ulong value)
        {
            uint rem = (uint)(value % 1_000_000_000);
            value /= 1_000_000_000;
            return rem;
        }

#if TARGET_64BIT
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        internal static unsafe TChar* UInt64ToDecChars<TChar>(TChar* bufferEnd, ulong value) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

#if TARGET_32BIT
            while ((uint)(value >> 32) != 0)
            {
                bufferEnd = UInt32ToDecChars(bufferEnd, Int64DivMod1E9(ref value), 9);
            }
            return UInt32ToDecChars(bufferEnd, (uint)value);
#else
            if (value >= 10)
            {
                // Handle all values >= 100 two-digits at a time so as to avoid expensive integer division operations.
                while (value >= 100)
                {
                    bufferEnd -= 2;
                    (value, ulong remainder) = Math.DivRem(value, 100);
                    WriteTwoDigits((uint)remainder, bufferEnd);
                }

                // If there are two digits remaining, store them.
                if (value >= 10)
                {
                    bufferEnd -= 2;
                    WriteTwoDigits((uint)value, bufferEnd);
                    return bufferEnd;
                }
            }

            // Otherwise, store the single digit remaining.
            *(--bufferEnd) = TChar.CastFrom(value + '0');
            return bufferEnd;
#endif
        }

#if TARGET_64BIT
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        internal static unsafe TChar* UInt64ToDecChars<TChar>(TChar* bufferEnd, ulong value, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

#if TARGET_32BIT
            while ((uint)(value >> 32) != 0)
            {
                bufferEnd = UInt32ToDecChars(bufferEnd, Int64DivMod1E9(ref value), 9);
                digits -= 9;
            }
            return UInt32ToDecChars(bufferEnd, (uint)value, digits);
#else
            ulong remainder;
            while (value >= 100)
            {
                bufferEnd -= 2;
                digits -= 2;
                (value, remainder) = Math.DivRem(value, 100);
                WriteTwoDigits((uint)remainder, bufferEnd);
            }

            while (value != 0 || digits > 0)
            {
                digits--;
                (value, remainder) = Math.DivRem(value, 10);
                *(--bufferEnd) = TChar.CastFrom(remainder + '0');
            }

            return bufferEnd;
#endif
        }

        internal static unsafe string UInt64ToDecStr(ulong value)
        {
            // For small numbers, consult a lazily-populated cache.
            if (value < SmallNumberCacheLength)
            {
                return UInt32ToDecStrForKnownSmallNumber((uint)value);
            }

            int bufferLength = FormattingHelpers.CountDigits(value);

            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = buffer + bufferLength;
                p = UInt64ToDecChars(p, value);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe string UInt64ToDecStr(ulong value, int digits)
        {
            if (digits <= 1)
            {
                return UInt64ToDecStr(value);
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits(value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = buffer + bufferLength;
                p = UInt64ToDecChars(p, value, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe bool TryUInt64ToDecStr<TChar>(ulong value, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            int bufferLength = FormattingHelpers.CountDigits(value);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
                {
                    TChar* p = buffer + bufferLength;
                    p = UInt64ToDecChars(p, value);
                    Debug.Assert(p == buffer);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }

        internal static unsafe bool TryUInt64ToDecStr<TChar>(ulong value, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            int countedDigits = FormattingHelpers.CountDigits(value);
            int bufferLength = Math.Max(digits, countedDigits);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
                {
                    TChar* p = buffer + bufferLength;
                    p = digits > countedDigits ?
                        UInt64ToDecChars(p, value, digits) :
                        UInt64ToDecChars(p, value);
                    Debug.Assert(p == buffer);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }

        private static unsafe void Int128ToNumber(Int128 value, ref NumberBuffer number)
        {
            number.DigitsCount = Int128Precision;

            if (Int128.IsPositive(value))
            {
                number.IsNegative = false;
            }
            else
            {
                number.IsNegative = true;
                value = -value;
            }

            byte* buffer = number.DigitsPtr;
            byte* p = UInt128ToDecChars(buffer + Int128Precision, (UInt128)value, 0);

            int i = (int)(buffer + Int128Precision - p);

            number.DigitsCount = i;
            number.Scale = i;

            byte* dst = number.DigitsPtr;
            while (--i >= 0)
            {
                *dst++ = *p++;
            }
            *dst = (byte)'\0';

            number.CheckConsistency();
        }

        public static string Int128ToDecStr(Int128 value)
        {
            return Int128.IsPositive(value)
                 ? UInt128ToDecStr((UInt128)value, -1)
                 : NegativeInt128ToDecStr(value, -1, NumberFormatInfo.CurrentNegativeSign);
        }

        private static unsafe string NegativeInt128ToDecStr(Int128 value, int digits, string sNegative)
        {
            Debug.Assert(Int128.IsNegative(value));

            if (digits < 1)
            {
                digits = 1;
            }

            UInt128 absValue = (UInt128)(-value);

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits(absValue)) + sNegative.Length;
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = UInt128ToDecChars(buffer + bufferLength, absValue, digits);
                Debug.Assert(p == buffer + sNegative.Length);

                for (int i = sNegative.Length - 1; i >= 0; i--)
                {
                    *(--p) = sNegative[i];
                }
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe bool TryNegativeInt128ToDecStr<TChar>(Int128 value, int digits, ReadOnlySpan<TChar> sNegative, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));
            Debug.Assert(Int128.IsNegative(value));

            if (digits < 1)
            {
                digits = 1;
            }

            UInt128 absValue = (UInt128)(-value);

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits(absValue)) + sNegative.Length;
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = UInt128ToDecChars(buffer + bufferLength, absValue, digits);
                Debug.Assert(p == buffer + sNegative.Length);

                for (int i = sNegative.Length - 1; i >= 0; i--)
                {
                    *(--p) = sNegative[i];
                }
                Debug.Assert(p == buffer);
            }
            return true;
        }

        private static unsafe string Int128ToHexStr(Int128 value, char hexBase, int digits)
        {
            if (digits < 1)
            {
                digits = 1;
            }

            UInt128 uValue = (UInt128)value;

            int bufferLength = Math.Max(digits, FormattingHelpers.CountHexDigits(uValue));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = Int128ToHexChars(buffer + bufferLength, uValue, hexBase, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe bool TryInt128ToHexStr<TChar>(Int128 value, char hexBase, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (digits < 1)
            {
                digits = 1;
            }

            UInt128 uValue = (UInt128)value;

            int bufferLength = Math.Max(digits, FormattingHelpers.CountHexDigits(uValue));
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = Int128ToHexChars(buffer + bufferLength, uValue, hexBase, digits);
                Debug.Assert(p == buffer);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe TChar* Int128ToHexChars<TChar>(TChar* buffer, UInt128 value, int hexBase, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            ulong lower = value.Lower;
            ulong upper = value.Upper;

            if (upper != 0)
            {
                buffer = Int64ToHexChars(buffer, lower, hexBase, 16);
                return Int64ToHexChars(buffer, upper, hexBase, digits - 16);
            }
            else
            {
                return Int64ToHexChars(buffer, lower, hexBase, Math.Max(digits, 1));
            }
        }

        private static unsafe string UInt128ToBinaryStr(Int128 value, int digits)
        {
            if (digits < 1)
            {
                digits = 1;
            }

            UInt128 uValue = (UInt128)value;

            int bufferLength = Math.Max(digits, 128 - (int)UInt128.LeadingZeroCount((UInt128)value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = UInt128ToBinaryChars(buffer + bufferLength, uValue, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe bool TryUInt128ToBinaryStr<TChar>(Int128 value, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            if (digits < 1)
            {
                digits = 1;
            }

            UInt128 uValue = (UInt128)value;

            int bufferLength = Math.Max(digits, 128 - (int)UInt128.LeadingZeroCount((UInt128)value));
            if (bufferLength > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = bufferLength;
            fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
            {
                TChar* p = UInt128ToBinaryChars(buffer + bufferLength, uValue, digits);
                Debug.Assert(p == buffer);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe TChar* UInt128ToBinaryChars<TChar>(TChar* buffer, UInt128 value, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            ulong lower = value.Lower;
            ulong upper = value.Upper;

            if (upper != 0)
            {
                buffer = UInt64ToBinaryChars(buffer, lower, 64);
                return UInt64ToBinaryChars(buffer, upper, digits - 64);
            }
            else
            {
                return UInt64ToBinaryChars(buffer, lower, Math.Max(digits, 1));
            }
        }

        internal static unsafe void UInt128ToNumber(UInt128 value, ref NumberBuffer number)
        {
            number.DigitsCount = UInt128Precision;
            number.IsNegative = false;

            byte* buffer = number.DigitsPtr;
            byte* p = UInt128ToDecChars(buffer + UInt128Precision, value, 0);

            int i = (int)(buffer + UInt128Precision - p);

            number.DigitsCount = i;
            number.Scale = i;

            byte* dst = number.DigitsPtr;
            while (--i >= 0)
            {
                *dst++ = *p++;
            }
            *dst = (byte)'\0';

            number.CheckConsistency();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Int128DivMod1E19(ref UInt128 value)
        {
            UInt128 divisor = new UInt128(0, 10_000_000_000_000_000_000);
            (value, UInt128 remainder) = UInt128.DivRem(value, divisor);
            return remainder.Lower;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe TChar* UInt128ToDecChars<TChar>(TChar* bufferEnd, UInt128 value) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            while (value.Upper != 0)
            {
                bufferEnd = UInt64ToDecChars(bufferEnd, Int128DivMod1E19(ref value), 19);
            }
            return UInt64ToDecChars(bufferEnd, value.Lower);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe TChar* UInt128ToDecChars<TChar>(TChar* bufferEnd, UInt128 value, int digits) where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

            while (value.Upper != 0)
            {
                bufferEnd = UInt64ToDecChars(bufferEnd, Int128DivMod1E19(ref value), 19);
                digits -= 19;
            }
            return UInt64ToDecChars(bufferEnd, value.Lower, digits);
        }

        internal static unsafe string UInt128ToDecStr(UInt128 value)
        {
            if (value.Upper == 0)
            {
                return UInt64ToDecStr(value.Lower);
            }

            int bufferLength = FormattingHelpers.CountDigits(value);

            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = buffer + bufferLength;
                p = UInt128ToDecChars(p, value);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        internal static unsafe string UInt128ToDecStr(UInt128 value, int digits)
        {
            if (digits <= 1)
            {
                return UInt128ToDecStr(value);
            }

            int bufferLength = Math.Max(digits, FormattingHelpers.CountDigits(value));
            string result = string.FastAllocateString(bufferLength);
            fixed (char* buffer = result)
            {
                char* p = buffer + bufferLength;
                p = UInt128ToDecChars(p, value, digits);
                Debug.Assert(p == buffer);
            }
            return result;
        }

        private static unsafe bool TryUInt128ToDecStr<TChar>(UInt128 value, int digits, Span<TChar> destination, out int charsWritten) where TChar : unmanaged, IUtfChar<TChar>
        {
            int countedDigits = FormattingHelpers.CountDigits(value);
            int bufferLength = Math.Max(digits, countedDigits);
            if (bufferLength <= destination.Length)
            {
                charsWritten = bufferLength;
                fixed (TChar* buffer = &MemoryMarshal.GetReference(destination))
                {
                    TChar* p = buffer + bufferLength;
                    p = digits > countedDigits ?
                        UInt128ToDecChars(p, value, digits) :
                        UInt128ToDecChars(p, value);
                    Debug.Assert(p == buffer);
                }
                return true;
            }

            charsWritten = 0;
            return false;
        }

    }
}
