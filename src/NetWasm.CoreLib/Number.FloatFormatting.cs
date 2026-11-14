// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce,
// System.Private.CoreLib Number.Formatting.cs (FormatFloat, GetFloatingPointMaxDigitsAndPrecision,
// FormatDecimal and TryFormatDecimal); FormatFloatingPointAsHex and ExtractFractionAndBiasedExponent
// are copied unchanged.
// The digits come from upstream's Grisu3, falling back to Dragon4, as upstream does (owner decision
// 2026-11-13, replacing NetWasm's exact rational generator, which allocated 4 KB to 30 KB per call).
// The layout is upstream's NumberToString/NumberToStringFormat with the caller's NumberFormatInfo, as on
// desktop .NET in invariant globalization mode.

using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System
{
    internal static partial class Number
    {
        internal static unsafe string FormatFloat<TNumber>(TNumber value, string? format, NumberFormatInfo info)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            char* stackPtr = stackalloc char[CharStackBufferSize];
            var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));
            string result = FormatFloat(ref vlb, value, format, info) ?? vlb.AsSpan().ToString();
            vlb.Dispose();
            return result;
        }

        // ToString(provider) and TryFormat with a provider but no format: the same shortest round-trip layout with
        // the provider's symbols. The format-string parser stays unreachable here too.
        internal static unsafe string FormatFloat<TNumber>(TNumber value, NumberFormatInfo info)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            char* stackPtr = stackalloc char[CharStackBufferSize];
            var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));
            string result = FormatFloatShortest(ref vlb, value, info) ?? vlb.AsSpan().ToString();
            vlb.Dispose();
            return result;
        }

        internal static unsafe bool TryFormatFloat<TNumber, TChar>(TNumber value, NumberFormatInfo info, Span<TChar> destination, out int charsWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            where TChar : unmanaged, IUtfChar<TChar>
        {
            TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
            var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));
            string? s = FormatFloatShortest(ref vlb, value, info);
            bool success = s != null ?
                TryCopyFloatSymbol(s, destination, out charsWritten) :
                vlb.TryCopyTo(destination, out charsWritten);
            vlb.Dispose();
            return success;
        }

        private static unsafe string? FormatFloatShortest<TNumber, TChar>(ref ValueListBuilder<TChar> vlb, TNumber value, NumberFormatInfo info)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            where TChar : unmanaged, IUtfChar<TChar>
        {
            byte* pDigits = stackalloc byte[TNumber.NumberBufferLength];
            NumberBuffer number = new NumberBuffer(NumberBufferKind.FloatingPoint, pDigits, TNumber.NumberBufferLength);
            switch (ShortestToNumber(value, ref number))
            {
                case FloatClass.NaN: return info.NaNSymbol;
                case FloatClass.PositiveInfinity: return info.PositiveInfinitySymbol;
                case FloatClass.NegativeInfinity: return info.NegativeInfinitySymbol;
            }
            if (number.IsNegative)
            {
                vlb.Append(info.NegativeSignTChar<TChar>());
            }
            FormatGeneral(ref vlb, ref number, Math.Max(number.DigitsCount, TNumber.MaxRoundTripDigits), info, 'E', suppressScientific: false);
            return null;
        }

        private enum FloatClass { Finite, NaN, PositiveInfinity, NegativeInfinity }

        // The shortest round-trip digits of a finite value into the number buffer, as NumberToString's 'G'
        // case at the round-trip precision would see them: they already stop at the shortest round-trip count,
        // so rounding to max(DigitsCount, MaxRoundTripDigits) changes nothing, and a floating-point -0 keeps
        // its sign.
        private static FloatClass ShortestToNumber<TNumber>(TNumber value, ref NumberBuffer number)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var bits = TNumber.FloatToBits(value);
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var signMask = 1UL << (fractionBits + TNumber.ExponentBits);
            var exponentMask = ((1UL << TNumber.ExponentBits) - 1) << fractionBits;
            var fractionMask = (1UL << fractionBits) - 1;
            var negative = (bits & signMask) != 0;
            var magnitude = bits & ~signMask;
            if ((magnitude & exponentMask) == exponentMask)
            {
                return (magnitude & fractionMask) != 0 ? FloatClass.NaN
                    : negative ? FloatClass.NegativeInfinity : FloatClass.PositiveInfinity;
            }
            number.IsNegative = negative;
            if (magnitude != 0 && !Grisu3.TryRun(value, -1, ref number))
            {
                Dragon4(value, -1, isSignificantDigits: true, ref number);
            }
            return FloatClass.Finite;
        }

        internal static unsafe bool TryFormatFloat<TNumber, TChar>(TNumber value, ReadOnlySpan<char> format, NumberFormatInfo info, Span<TChar> destination, out int charsWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            where TChar : unmanaged, IUtfChar<TChar>
        {
            TChar* stackPtr = stackalloc TChar[CharStackBufferSize];
            var vlb = new ValueListBuilder<TChar>(new Span<TChar>(stackPtr, CharStackBufferSize));
            string? s = FormatFloat(ref vlb, value, format, info);
            bool success = s != null ?
                TryCopyFloatSymbol(s, destination, out charsWritten) :
                vlb.TryCopyTo(destination, out charsWritten);
            vlb.Dispose();
            return success;
        }

        /// <summary>Formats the specified value according to the specified format and info.</summary>
        /// <returns>
        /// Non-null if an existing string can be returned, in which case the builder will be unmodified.
        /// Null if no existing string was returned, in which case the formatted output is in the builder.
        /// </returns>
        private static unsafe string? FormatFloat<TNumber, TChar>(ref ValueListBuilder<TChar> vlb, TNumber value, ReadOnlySpan<char> format, NumberFormatInfo info)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            where TChar : unmanaged, IUtfChar<TChar>
        {
            var bits = TNumber.FloatToBits(value);
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var signMask = 1UL << (fractionBits + TNumber.ExponentBits);
            var exponentMask = ((1UL << TNumber.ExponentBits) - 1) << fractionBits;
            var fractionMask = (1UL << fractionBits) - 1;
            var negative = (bits & signMask) != 0;
            var magnitude = bits & ~signMask;

            if ((magnitude & exponentMask) == exponentMask)
            {
                return (magnitude & fractionMask) != 0
                    ? info.NaNSymbol
                    : negative ? info.NegativeInfinitySymbol : info.PositiveInfinitySymbol;
            }

            char fmt = ParseFormatSpecifier(format, out int precision);

            if ((fmt | 0x20) == 'x')
            {
                FormatFloatingPointAsHex(ref vlb, value, fmt, precision, info);
                return null;
            }

            byte* pDigits = stackalloc byte[TNumber.NumberBufferLength];

            if (fmt == '\0')
            {
                precision = TNumber.MaxPrecisionCustomFormat;
            }

            NumberBuffer number = new NumberBuffer(NumberBufferKind.FloatingPoint, pDigits, TNumber.NumberBufferLength);
            number.IsNegative = negative;

            // We need to track the original precision requested since some formats
            // accept values like 0 and others may require additional fixups.
            int nMaxDigits = GetFloatingPointMaxDigitsAndPrecision(fmt, ref precision, info, out bool isSignificantDigits);

            if (magnitude != 0 && (!isSignificantDigits || !Grisu3.TryRun(value, precision, ref number)))
            {
                Dragon4(value, precision, isSignificantDigits, ref number);
            }

            number.CheckConsistency();

            if (fmt != 0)
            {
                if (precision == -1)
                {
                    // For the roundtrip and general format specifiers, when returning the shortest roundtrippable
                    // string, we need to update the maximum number of digits to be the greater of number.DigitsCount
                    // or the round-trip precision. This ensures that we continue returning "pretty" strings for
                    // values with less digits, such as "-60" rather than "-6E+01".
                    nMaxDigits = Math.Max(number.DigitsCount, TNumber.MaxRoundTripDigits);
                }
                NumberToString(ref vlb, ref number, fmt, nMaxDigits, info);
            }
            else
            {
                NumberToStringFormat(ref vlb, ref number, format, info);
            }
            return null;
        }

        private static int GetFloatingPointMaxDigitsAndPrecision(char fmt, ref int precision, NumberFormatInfo info, out bool isSignificantDigits)
        {
            // We want to fast path the common case of no format and general format + precision.
            // These are commonly encountered and the full switch is otherwise large enough to show up in hot path profiles

            if (fmt == 0)
            {
                isSignificantDigits = true;
                return precision;
            }

            // Bitwise-or with space (' ') converts any uppercase character to
            // lowercase and keeps unsupported characters as something unsupported.
            fmt |= ' ';

            if (fmt == 'g')
            {
                // The general format uses the precision specifier to indicate the number of significant
                // digits to format. This defaults to the shortest roundtrippable string. Additionally,
                // given that we can't return zero significant digits, we treat 0 as returning the shortest
                // roundtrippable string as well.

                isSignificantDigits = true;

                if (precision == 0)
                {
                    precision = -1;
                    return 0;
                }
                return precision;
            }

            return Slow(fmt, ref precision, info, out isSignificantDigits);

            static int Slow(char fmt, ref int precision, NumberFormatInfo info, out bool isSignificantDigits)
            {
                int maxDigits = precision;

                switch (fmt)
                {
                    case 'c':
                        {
                            // The currency format uses the precision specifier to indicate the number of
                            // decimal digits to format. This defaults to NumberFormatInfo.CurrencyDecimalDigits.

                            if (precision == -1)
                            {
                                precision = info.CurrencyDecimalDigits;
                            }
                            isSignificantDigits = false;

                            break;
                        }

                    case 'e':
                        {
                            // The exponential format uses the precision specifier to indicate the number of
                            // decimal digits to format. This defaults to 6. However, the exponential format
                            // also always formats a single integral digit, so we need to increase the precision
                            // specifier and treat it as the number of significant digits to account for this.

                            if (precision == -1)
                            {
                                precision = DefaultPrecisionExponentialFormat;
                            }

                            precision++;
                            isSignificantDigits = true;

                            break;
                        }

                    case 'f':
                    case 'n':
                        {
                            // The fixed-point and number formats use the precision specifier to indicate the number
                            // of decimal digits to format. This defaults to NumberFormatInfo.NumberDecimalDigits.

                            if (precision == -1)
                            {
                                precision = info.NumberDecimalDigits;
                            }
                            isSignificantDigits = false;

                            break;
                        }

                    case 'p':
                        {
                            // The percent format uses the precision specifier to indicate the number of
                            // decimal digits to format. This defaults to NumberFormatInfo.PercentDecimalDigits.
                            // However, the percent format also always multiplies the number by 100, so we need
                            // to increase the precision specifier to ensure we get the appropriate number of digits.

                            if (precision == -1)
                            {
                                precision = info.PercentDecimalDigits;
                            }

                            precision += 2;
                            isSignificantDigits = false;

                            break;
                        }

                    case 'r':
                        {
                            // The roundtrip format ignores the precision specifier and always returns the shortest
                            // roundtrippable string.

                            precision = -1;
                            isSignificantDigits = true;

                            break;
                        }

                    default:
                        {
                            ThrowHelper.ThrowFormatException_BadFormatSpecifier();
                            goto case 'r'; // unreachable
                        }
                }

                return maxDigits;
            }
        }

        private static bool TryCopyFloatSymbol<TChar>(string source, Span<TChar> destination, out int charsWritten)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (typeof(TChar) == typeof(char))
            {
                if (source.AsSpan().TryCopyTo(Unsafe.BitCast<Span<TChar>, Span<char>>(destination)))
                {
                    charsWritten = source.Length;
                    return true;
                }

                charsWritten = 0;
                return false;
            }

            return TryCopyUtf8(source, Unsafe.BitCast<Span<TChar>, Span<byte>>(destination), out charsWritten);
        }

        internal static unsafe string FormatDecimal(decimal value, ReadOnlySpan<char> format, NumberFormatInfo info)
        {
            char fmt = ParseFormatSpecifier(format, out int digits);

            byte* pDigits = stackalloc byte[DecimalNumberBufferLength];
            NumberBuffer number = new NumberBuffer(NumberBufferKind.Decimal, pDigits, DecimalNumberBufferLength);

            DecimalToNumber(ref value, ref number);

            char* stackPtr = stackalloc char[CharStackBufferSize];
            var vlb = new ValueListBuilder<char>(new Span<char>(stackPtr, CharStackBufferSize));

            if (fmt != 0)
            {
                NumberToString(ref vlb, ref number, fmt, digits, info);
            }
            else
            {
                NumberToStringFormat(ref vlb, ref number, format, info);
            }

            string result = vlb.AsSpan().ToString();
            vlb.Dispose();
            return result;
        }

        internal static unsafe bool TryFormatDecimal<TChar>(decimal value, ReadOnlySpan<char> format, NumberFormatInfo info, Span<TChar> destination, out int charsWritten)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            char fmt = ParseFormatSpecifier(format, out int digits);

            byte* pDigits = stackalloc byte[DecimalNumberBufferLength];
            NumberBuffer number = new NumberBuffer(NumberBufferKind.Decimal, pDigits, DecimalNumberBufferLength);

            DecimalToNumber(ref value, ref number);

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

        private static unsafe void FormatFloatingPointAsHex<TNumber, TChar>(ref ValueListBuilder<TChar> vlb, TNumber value, char fmt, int precision, NumberFormatInfo info)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            where TChar : unmanaged, IUtfChar<TChar>
        {
            Debug.Assert((fmt | 0x20) == 'x');
            Debug.Assert(TNumber.IsFinite(value));

            bool isNegative = TNumber.IsNegative(value);

            if (isNegative)
            {
                vlb.Append(info.NegativeSignTChar<TChar>());
            }

            vlb.Append(TChar.CastFrom('0'));
            vlb.Append(TChar.CastFrom(fmt));

            ulong fraction = ExtractFractionAndBiasedExponent(value, out int exponent);

            if (fraction == 0)
            {
                // +/- 0
                vlb.Append(TChar.CastFrom('0'));

                if (precision > 0)
                {
                    vlb.Append(info.NumberDecimalSeparatorTChar<TChar>());
                    vlb.AppendSpan(precision).Fill(TChar.CastFrom('0'));
                }

                vlb.Append(TChar.CastFrom(fmt == 'X' ? 'P' : 'p'));
                vlb.Append(info.PositiveSignTChar<TChar>());
                vlb.Append(TChar.CastFrom('0'));

                return;
            }

            // ExtractFractionAndBiasedExponent returns (note: despite the name, the exponent is unbiased):
            //   For normal:   fraction = (1 << DenormalMantissaBits) | mantissa, exponent = biasedExp - ExponentBias - DenormalMantissaBits
            //   For denormal: fraction = mantissa, exponent = MinBinaryExponent - DenormalMantissaBits
            //
            // We want the form: 1.xxxxx * 2^e
            // So we need to normalize so that the leading 1 bit is at bit DenormalMantissaBits.
            // For normal numbers, this is already the case.
            // For denormal numbers, we need to shift left until the leading 1 is there.

            int mantissaBits = TNumber.DenormalMantissaBits;

            if (fraction < (1UL << mantissaBits))
            {
                // Denormal: shift the leading 1 up to the implicit bit position
                int lz = BitOperations.LeadingZeroCount(fraction) - (63 - mantissaBits);
                fraction <<= lz;
                exponent -= lz;
            }

            // Now fraction has the leading 1 at bit [mantissaBits], and the remaining bits below.
            // The unbiased exponent for the value is: exponent + mantissaBits (since fraction is
            // really fraction * 2^exponent, and we want 1.xxx * 2^actualExponent).
            int actualExponent = exponent + mantissaBits;

            // Strip the implicit leading 1 to get the fractional bits
            ulong significandBits = fraction & ((1UL << mantissaBits) - 1);

            // Leading digit is normally '1' for non-zero (the implicit bit)
            int leadingDigit = 1;

            // Determine how many hex digits to emit for the fractional part
            int defaultHexDigits = (mantissaBits + 3) / 4;

            if (precision == 0)
            {
                // Round significandBits into the leading digit
                ulong half = (mantissaBits > 0) ? (1UL << (mantissaBits - 1)) : 0;
                if (significandBits > half || (significandBits == half && (leadingDigit & 1) != 0))
                {
                    leadingDigit++;
                    // leadingDigit can't exceed 2 since it started at 1
                }

                significandBits = 0;
            }

            vlb.Append(TChar.CastFrom((char)('0' + leadingDigit)));

            if (precision > 0)
            {
                ulong shifted;

                if (precision < defaultHexDigits)
                {
                    // Need to round
                    int bitsToKeep = precision * 4;
                    int bitsToDiscard = mantissaBits - bitsToKeep;

                    // bitsToDiscard is always in (0, mantissaBits) here because precision >= 1
                    // (we're in the precision > 0 branch) and precision < defaultHexDigits
                    // (checked above), so bitsToKeep < mantissaBits and bitsToDiscard > 0.
                    // For all IEEE types mantissaBits <= 52, so bitsToDiscard < 64.
                    Debug.Assert(bitsToDiscard > 0 && bitsToDiscard < 64);
                    if (bitsToDiscard > 0 && bitsToDiscard < 64)
                    {
                        ulong roundBit = 1UL << (bitsToDiscard - 1);
                        ulong discardedBits = significandBits & ((1UL << bitsToDiscard) - 1);
                        bool roundUp = discardedBits > roundBit || (discardedBits == roundBit && ((significandBits >> bitsToDiscard) & 1) != 0);

                        if (roundUp)
                        {
                            significandBits = (significandBits >> bitsToDiscard) + 1;

                            // Check if rounding overflowed into leading digit
                            if (significandBits >= (1UL << bitsToKeep))
                            {
                                significandBits = 0;
                                actualExponent++;
                            }
                        }
                        else
                        {
                            significandBits >>= bitsToDiscard;
                        }

                        shifted = significandBits << (64 - bitsToKeep);
                    }
                    else
                    {
                        shifted = significandBits << (64 - mantissaBits);
                    }
                }
                else
                {
                    shifted = significandBits << (64 - mantissaBits);
                }

                vlb.Append(info.NumberDecimalSeparatorTChar<TChar>());

                // Emit real nibbles
                int realDigits = Math.Min(precision, defaultHexDigits);
                for (int i = 0; i < realDigits; i++)
                {
                    vlb.Append(TChar.CastFrom(fmt == 'X' ? HexConverter.ToCharUpper((int)(shifted >> 60)) : HexConverter.ToCharLower((int)(shifted >> 60))));
                    shifted <<= 4;
                }

                // Emit padding zeros (when precision > defaultHexDigits)
                int padCount = precision - realDigits;
                if (padCount > 0)
                {
                    vlb.AppendSpan(padCount).Fill(TChar.CastFrom('0'));
                }
            }
            else if (precision < 0)
            {
                // Default precision: emit significant hex digits, trimming trailing zeros.
                // Compute trailing zero nibbles from the nibble-aligned representation.
                int trimmedDigits = 0;
                if (significandBits != 0)
                {
                    // Align significand to nibble boundary (pad LSB so total bits = defaultHexDigits * 4),
                    // then count trailing zero nibbles via trailing zero bits.
                    int paddingBits = defaultHexDigits * 4 - mantissaBits;
                    ulong nibbleAligned = significandBits << paddingBits;
                    int trailingZeroBits = BitOperations.TrailingZeroCount(nibbleAligned);
                    trimmedDigits = defaultHexDigits - (trailingZeroBits / 4);

                    if (trimmedDigits > 0)
                    {
                        vlb.Append(info.NumberDecimalSeparatorTChar<TChar>());

                        ulong shifted = significandBits << (64 - mantissaBits);
                        for (int i = 0; i < trimmedDigits; i++)
                        {
                            vlb.Append(TChar.CastFrom(fmt == 'X' ? HexConverter.ToCharUpper((int)(shifted >> 60)) : HexConverter.ToCharLower((int)(shifted >> 60))));
                            shifted <<= 4;
                        }
                    }
                }
            }

            vlb.Append(TChar.CastFrom(fmt == 'X' ? 'P' : 'p'));

            if (actualExponent >= 0)
            {
                vlb.Append(info.PositiveSignTChar<TChar>());
            }
            else
            {
                vlb.Append(info.NegativeSignTChar<TChar>());
                actualExponent = -actualExponent;
            }

            Debug.Assert(actualExponent >= 0);
            int digitCount = FormattingHelpers.CountDigits((uint)actualExponent);
            TChar* pExponent = stackalloc TChar[digitCount];
            UInt32ToDecChars(pExponent + digitCount, (uint)actualExponent);
            vlb.Append(new ReadOnlySpan<TChar>(pExponent, digitCount));
        }

        private static ulong ExtractFractionAndBiasedExponent<TNumber>(TNumber value, out int exponent)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            ulong bits = TNumber.FloatToBits(value);
            ulong fraction = (bits & TNumber.DenormalMantissaMask);
            exponent = ((int)(bits >> TNumber.DenormalMantissaBits) & TNumber.InfinityExponent);

            if (exponent != 0)
            {
                // For normalized value,
                // value = 1.fraction * 2^(exp - ExponentBias)
                //       = (1 + mantissa / 2^TrailingSignificandLength) * 2^(exp - ExponentBias)
                //       = (2^TrailingSignificandLength + mantissa) * 2^(exp - ExponentBias - TrailingSignificandLength)
                //
                // So f = (2^TrailingSignificandLength + mantissa), e = exp - ExponentBias - TrailingSignificandLength;

                fraction |= (1UL << TNumber.DenormalMantissaBits);
                exponent -= TNumber.ExponentBias + TNumber.DenormalMantissaBits;
            }
            else
            {
                // For denormalized value,
                // value = 0.fraction * 2^(MinBinaryExponent)
                //       = (mantissa / 2^TrailingSignificandLength) * 2^(MinBinaryExponent)
                //       = mantissa * 2^(MinBinaryExponent - TrailingSignificandLength)
                //       = mantissa * 2^(MinBinaryExponent - TrailingSignificandLength)
                // So f = mantissa, e = MinBinaryExponent - TrailingSignificandLength
                exponent = TNumber.MinBinaryExponent - TNumber.DenormalMantissaBits;
            }

            return fraction;
        }
    }
}
