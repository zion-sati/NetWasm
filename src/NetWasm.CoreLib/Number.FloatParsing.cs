// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce,
// System.Private.CoreLib Number.Parsing.cs (ParseFloat, TryParseFloat, TryParseHexFloatingPoint, NumberToFloat, ParseDecimal,
// TryParseDecimal, SpanStartsWith, SpanEqualsOrdinalIgnoreCase, SpanTrimStart and TryMatchSpecialValueSymbol).
// NetWasm adaptations:
// - NumberToFloat converts the number buffer's digits with NetWasm's exact ConvertDecimalToFloatingBits
//   instead of upstream's Eisel-Lemire and big-number paths; a truncated buffer contributes a sticky digit,
//   so rounding is the same.
// - TryParseHexFloatingPoint is upstream's (.NET 11's hexadecimal floating point), unchanged.
// - The span helpers use the public ordinal span APIs; the UTF-8 forms decode to UTF-16 first, which only
//   happens on the special-value path.

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace System
{
    internal static partial class Number
    {
        internal static TFloat ParseFloat<TChar, TFloat>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info)
            where TChar : unmanaged, IUtfChar<TChar>
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            if (!TryParseFloat(value, styles, info, out TFloat result, out _))
            {
                throw new FormatException();
            }
            return result;
        }

        internal static bool TryParseHexFloatingPoint<TChar, TFloat>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info, out TFloat result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            result = TFloat.Zero;

            if (value.IsEmpty)
            {
                elementsConsumed = 0;
                return false;
            }

            int index = 0;

            // Skip leading whitespace
            if ((styles & NumberStyles.AllowLeadingWhite) != 0)
            {
                while (index < value.Length && IsWhite(TChar.CastToUInt32(value[index])))
                {
                    index++;
                }
            }

            if (index >= value.Length)
            {
                elementsConsumed = 0;
                return false;
            }

            // Parse optional sign
            bool isNegative = false;
            if ((styles & NumberStyles.AllowLeadingSign) != 0)
            {
                int nextIndex = MatchSignChars(value, index, info, out isNegative);
                if (nextIndex >= 0)
                {
                    index = nextIndex;
                }
            }

            if (index >= value.Length)
            {
                elementsConsumed = 0;
                return false;
            }

            // Require "0x" or "0X" prefix (consistent with IEEE 754 conventions)
            if (TChar.CastToUInt32(value[index]) != '0' ||
                index + 1 >= value.Length ||
                (TChar.CastToUInt32(value[index + 1]) | 0x20) != 'x')
            {
                elementsConsumed = 0;
                return false;
            }
            index += 2;

            if (index >= value.Length)
            {
                elementsConsumed = 0;
                return false;
            }

            // Parse hex significand.
            // We accumulate up to 16 significant hex digits into a ulong.
            // We track the exponent adjustment due to digit position.
            //
            // The value is: significand * 2^(binaryExponent - 4 * fractionalDigitsConsumed + 4 * overflowIntegerDigits)

            ulong significand = 0;
            int significandDigits = 0;       // Count of significant (non-leading-zero) digits consumed into significand
            int overflowIntegerDigits = 0;   // Integer digits that didn't fit
            bool hasDiscardedNonZeroDigits = false;  // IEEE 754 "sticky bit": any nonzero digit discarded beyond significand capacity

            int integerPartStart = index;
            while (index < value.Length)
            {
                uint ch = TChar.CastToUInt32(value[index]);
                int digit = HexConverter.FromChar((int)ch);
                if (digit >= 16)
                {
                    break;
                }

                // Accumulate up to 16 significant hex digits. The '|| significand == 0' is
                // a defensive check: significandDigits only increments when a nonzero digit is
                // accumulated, so significandDigits >= 16 implies significand != 0 in practice.
                if (significandDigits < 16 || significand == 0)
                {
                    if (significand != 0 || digit != 0)
                    {
                        significand = (significand << 4) | (uint)digit;
                        significandDigits++;
                    }
                }
                else
                {
                    overflowIntegerDigits++;
                    hasDiscardedNonZeroDigits |= digit != 0;
                }

                index++;
            }
            bool hasIntegerPart = index > integerPartStart;

            // Parse fractional part
            int fractionalDigitsConsumed = 0;
            bool hasFractionalPart = false;

            if ((styles & NumberStyles.AllowDecimalPoint) != 0 && index < value.Length)
            {
                ReadOnlySpan<TChar> decimalSeparator = info.NumberDecimalSeparatorTChar<TChar>();
                if (value.Slice(index).StartsWith(decimalSeparator))
                {
                    index += decimalSeparator.Length;

                    int fractionalPartStart = index;
                    while (index < value.Length)
                    {
                        uint ch = TChar.CastToUInt32(value[index]);
                        int digit = HexConverter.FromChar((int)ch);
                        if (digit >= 16)
                        {
                            break;
                        }

                        // Accumulate significant digits (see integer loop comment for '|| significand == 0').
                        // Discarded fractional digits intentionally do NOT increment fractionalDigitsConsumed:
                        // they are beyond significand precision and only contribute sticky bits for rounding.
                        if (significandDigits < 16 || significand == 0)
                        {
                            if (significand != 0 || digit != 0)
                            {
                                significand = (significand << 4) | (uint)digit;
                                significandDigits++;
                            }

                            // Always increment, even for leading zeros: positional value matters
                            // (e.g., 0x0.004p0 = 4 * 2^-12, so all three fractional digits count).
                            fractionalDigitsConsumed++;
                        }
                        else
                        {
                            hasDiscardedNonZeroDigits |= digit != 0;
                        }

                        index++;
                    }
                    hasFractionalPart = index > fractionalPartStart;
                }
            }

            if (!hasIntegerPart && !hasFractionalPart)
            {
                elementsConsumed = 0;
                return false;
            }

            // Parse the exponent: 'p' or 'P' followed by optional sign and decimal digits.
            // The decimal value specifies an exponent in the radix of the floating-point format
            // (for binary types, the value is multiplied by 2 raised to this power).
            int binaryExponent = 0;
            if (index < value.Length && ((TChar.CastToUInt32(value[index]) | 0x20) == 'p'))
            {
                index++;

                if (index >= value.Length)
                {
                    elementsConsumed = 0;
                    return false;
                }

                bool exponentIsNegative;
                int nextIndex = MatchSignChars(value, index, info, out exponentIsNegative);
                if (nextIndex >= 0)
                {
                    index = nextIndex;
                }

                if (index >= value.Length)
                {
                    elementsConsumed = 0;
                    return false;
                }

                int exponentStart = index;
                while (index < value.Length)
                {
                    uint ech = TChar.CastToUInt32(value[index]);
                    if (!IsDigit(ech))
                    {
                        break;
                    }

                    int digit = (int)(ech - '0');

                    // Saturate at int.MaxValue on overflow. Unlike the significand (which tracks
                    // overflow digits and sticky bits for rounding), the exponent just needs to be
                    // large enough to guarantee the result resolves to infinity or zero.
                    binaryExponent = binaryExponent <= (int.MaxValue - digit) / 10 ?
                        binaryExponent * 10 + digit :
                        int.MaxValue;

                    index++;
                }

                if (index == exponentStart)
                {
                    elementsConsumed = 0;
                    return false;
                }

                if (exponentIsNegative)
                {
                    binaryExponent = -binaryExponent;
                }
            }
            else
            {
                // Exponent indicator (p/P) is required
                elementsConsumed = 0;
                return false;
            }

            // Skip trailing whitespace
            if ((styles & NumberStyles.AllowTrailingWhite) != 0)
            {
                while (index < value.Length && IsWhite(TChar.CastToUInt32(value[index])))
                {
                    index++;
                }
            }

            // For compatibility we still need to process any trailing
            // nulls that exist and report them as having been consumed.

            index = ConsumeTrailingNulls(value, index);

            if ((index != value.Length) && ((styles & AllowTrailingInvalidCharacters) == 0))
            {
                elementsConsumed = 0;
                return false;
            }

            // We've successfully parsed a number, so now we just need to handle constructing the result
            elementsConsumed = index;

            if (significand == 0)
            {
                result = isNegative ? TFloat.NegativeZero : TFloat.Zero;
                return true;
            }

            // Compute the effective binary exponent.
            // value = significand * 2^(-4 * fractionalDigitsConsumed) * 2^(4 * overflowIntegerDigits) * 2^binaryExponent
            long exp = (long)binaryExponent - 4L * fractionalDigitsConsumed + 4L * overflowIntegerDigits;

            // Normalize: shift significand so MSB is at bit 63
            int lz = BitOperations.LeadingZeroCount(significand);
            significand <<= lz;
            exp -= lz;

            // significand is now in [2^63, 2^64), so value = significand * 2^exp
            // = (significand / 2^63) * 2^(exp + 63) = 1.xxx * 2^(exp + 63)
            long actualExp = exp + 63;

            int mantissaBits = TFloat.DenormalMantissaBits;

            if (actualExp > TFloat.MaxBinaryExponent)
            {
                result = isNegative ? TFloat.NegativeInfinity : TFloat.PositiveInfinity;
                return true;
            }

            int shiftRight = 63 - mantissaBits;
            Debug.Assert(shiftRight >= 11, "shiftRight is always >= 11 for all IEEE float types (double: 11, float: 40, Half: 53, BFloat16: 56)");
            long biasedExp = actualExp + TFloat.ExponentBias;

            if (biasedExp <= 0)
            {
                long denormalShift = 1L - biasedExp;
                if (denormalShift > 64 - shiftRight)
                {
                    // Value is too small to round to min subnormal
                    result = isNegative ? TFloat.NegativeZero : TFloat.Zero;
                    return true;
                }
                shiftRight += (int)denormalShift;
                biasedExp = 0;
            }

            // Round to nearest, ties to even
            ulong mantissa = 0;
            if (shiftRight > 0 && shiftRight < 64)
            {
                ulong roundBit = 1UL << (shiftRight - 1);
                ulong stickyBits = (significand & (roundBit - 1)) | (hasDiscardedNonZeroDigits ? 1UL : 0UL);
                mantissa = significand >> shiftRight;

                if ((significand & roundBit) != 0 && (stickyBits != 0 || (mantissa & 1) != 0))
                {
                    mantissa++;

                    if (biasedExp == 0 && mantissa > TFloat.DenormalMantissaMask)
                    {
                        biasedExp = 1;
                        mantissa &= TFloat.DenormalMantissaMask;
                    }
                    else if (mantissa > ((1UL << (mantissaBits + 1)) - 1))
                    {
                        mantissa >>= 1;
                        biasedExp++;
                        if (biasedExp >= TFloat.InfinityExponent)
                        {
                            result = isNegative ? TFloat.NegativeInfinity : TFloat.PositiveInfinity;
                            return true;
                        }
                    }
                }
            }
            else if (shiftRight == 64)
            {
                // Significand is at bit 63. Round bit is bit 63, sticky bits are 62..0.
                ulong roundBit = 1UL << 63;
                ulong stickyBits = (significand & (roundBit - 1)) | (hasDiscardedNonZeroDigits ? 1UL : 0UL);
                mantissa = 0;

                // mantissa is 0 (even), so ties-to-even rounds up only when sticky bits are nonzero.
                if ((significand & roundBit) != 0 && stickyBits != 0)
                {
                    mantissa = 1;
                    if (mantissa > TFloat.DenormalMantissaMask)
                    {
                        biasedExp = 1;
                        mantissa &= TFloat.DenormalMantissaMask;
                    }
                }
            }
            // shiftRight > 64 is impossible: max is 63 - 7 + denormalShift, capped by the
            // early return when denormalShift > 64 - shiftRight.
            // shiftRight == 0 is impossible: minimum is 63 - 52 = 11 (for double), see assert above.
            Debug.Assert(shiftRight > 0 && shiftRight <= 64);

            mantissa &= TFloat.DenormalMantissaMask;

            ulong bits = ((ulong)biasedExp << mantissaBits) | mantissa;
            result = TFloat.BitsToFloat(bits);

            if (isNegative)
            {
                result = -result;
            }
            return true;
        }

        internal static unsafe bool TryParseFloat<TChar, TFloat>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info, out TFloat result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            if ((styles & NumberStyles.AllowHexSpecifier) != 0)
            {
                if (TryParseHexFloatingPoint(value, styles, info, out result, out elementsConsumed))
                {
                    return true;
                }
            }
            else
            {
                byte* pDigits = stackalloc byte[TFloat.NumberBufferLength];
                NumberBuffer number = new NumberBuffer(NumberBufferKind.FloatingPoint, pDigits, TFloat.NumberBufferLength);

                if (TryStringToNumber(value, styles, ref number, info, out elementsConsumed))
                {
                    result = NumberToFloat<TFloat>(ref number);
                    return true;
                }
            }

            // Both the hex and non-hex floating-point paths support the Infinity/NaN symbols, so
            // either falls through to the special-value handling below when the numeric parse fails.
            //
            // Leading and trailing whitespace around a special value (Infinity/NaN) is always
            // consumed, independent of the AllowLeadingWhite/AllowTrailingWhite styles (historical
            // precedent). When AllowTrailingInvalidCharacters is set, parsing additionally stops at
            // the first non-whitespace character after the symbol; otherwise such a trailing
            // character rejects the match.
            ReadOnlySpan<TChar> valueTrim = SpanTrimStart(value);
            bool allowTrailingInvalid = (styles & AllowTrailingInvalidCharacters) != 0;

            // elementsConsumed is seeded with the offset of the candidate within value (the leading
            // whitespace, plus any sign) and then advanced by TryMatchSpecialValueSymbol on a match.
            elementsConsumed = value.Length - valueTrim.Length;

            // This code would be simpler if we only had the concept of `InfinitySymbol`, but
            // we don't so we'll check the existing cases first and then handle `PositiveSign` +
            // `PositiveInfinitySymbol` and `PositiveSign/NegativeSign` + `NaNSymbol` last.

            ReadOnlySpan<TChar> positiveInfinitySymbol = info.PositiveInfinitySymbolTChar<TChar>();

            if (TryMatchSpecialValueSymbol(valueTrim, positiveInfinitySymbol, allowTrailingInvalid, ref elementsConsumed))
            {
                result = TFloat.PositiveInfinity;
                return true;
            }

            if (TryMatchSpecialValueSymbol(valueTrim, info.NegativeInfinitySymbolTChar<TChar>(), allowTrailingInvalid, ref elementsConsumed))
            {
                result = TFloat.NegativeInfinity;
                return true;
            }

            ReadOnlySpan<TChar> nanSymbol = info.NaNSymbolTChar<TChar>();

            if (TryMatchSpecialValueSymbol(valueTrim, nanSymbol, allowTrailingInvalid, ref elementsConsumed))
            {
                result = TFloat.NaN;
                return true;
            }

            ReadOnlySpan<TChar> positiveSign = info.PositiveSignTChar<TChar>();

            if (SpanStartsWith(valueTrim, positiveSign, StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<TChar> afterSign = valueTrim.Slice(positiveSign.Length);
                elementsConsumed = value.Length - afterSign.Length;

                if (TryMatchSpecialValueSymbol(afterSign, positiveInfinitySymbol, allowTrailingInvalid, ref elementsConsumed))
                {
                    result = TFloat.PositiveInfinity;
                    return true;
                }
                else if (TryMatchSpecialValueSymbol(afterSign, nanSymbol, allowTrailingInvalid, ref elementsConsumed))
                {
                    result = TFloat.NaN;
                    return true;
                }

                result = TFloat.Zero;
                elementsConsumed = 0;
                return false;
            }

            ReadOnlySpan<TChar> negativeSign = info.NegativeSignTChar<TChar>();

            if (SpanStartsWith(valueTrim, negativeSign, StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<TChar> afterSign = valueTrim.Slice(negativeSign.Length);
                elementsConsumed = value.Length - afterSign.Length;

                if (TryMatchSpecialValueSymbol(afterSign, nanSymbol, allowTrailingInvalid, ref elementsConsumed))
                {
                    result = TFloat.NaN;
                    return true;
                }

                if (info.AllowHyphenDuringParsing() && SpanStartsWith(valueTrim, TChar.CastFrom('-')))
                {
                    ReadOnlySpan<TChar> afterHyphen = valueTrim.Slice(1);
                    elementsConsumed = value.Length - afterHyphen.Length;

                    if (TryMatchSpecialValueSymbol(afterHyphen, nanSymbol, allowTrailingInvalid, ref elementsConsumed))
                    {
                        result = TFloat.NaN;
                        return true;
                    }
                }
            }

            result = TFloat.Zero;
            elementsConsumed = 0;
            return false; // We really failed
        }

        internal static decimal ParseDecimal<TChar>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            ParsingStatus status = TryParseDecimal(value, styles, info, out decimal result, out _);

            if (status != ParsingStatus.OK)
            {
                if (status == ParsingStatus.Failed)
                {
                    throw new FormatException();
                }
                ThrowDecimalOverflowException();
            }

            return result;
        }

        internal static unsafe ParsingStatus TryParseDecimal<TChar>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info, out decimal result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            byte* pDigits = stackalloc byte[DecimalNumberBufferLength];
            NumberBuffer number = new NumberBuffer(NumberBufferKind.Decimal, pDigits, DecimalNumberBufferLength);

            result = 0;

            if (!TryStringToNumber(value, styles, ref number, info, out elementsConsumed))
            {
                return ParsingStatus.Failed;
            }

            if (!TryNumberToDecimal(ref number, ref result))
            {
                elementsConsumed = 0;
                return ParsingStatus.Overflow;
            }

            return ParsingStatus.OK;
        }

        internal static TFloat NumberToFloat<TFloat>(ref NumberBuffer number)
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            number.CheckConsistency();
            TFloat result;

            if ((number.DigitsCount == 0) || (number.Scale < TFloat.MinDecimalExponent))
            {
                result = TFloat.Zero;
            }
            else if (number.Scale > TFloat.MaxDecimalExponent)
            {
                result = TFloat.PositiveInfinity;
            }
            else
            {
                // The buffer holds the significant digits d1..dn with the decimal point after Scale of them,
                // so the value is d1..dn * 10^(Scale - n). Digits dropped beyond the buffer only matter as a
                // non-zero tail, which a trailing 1 represents exactly enough for correct rounding.
                int digitCount = number.DigitsCount;
                int length = number.HasNonZeroTail ? digitCount + 1 : digitCount;
                Span<char> digits = length <= 128 ? stackalloc char[length] : new char[length];
                for (int i = 0; i < digitCount; i++)
                {
                    digits[i] = (char)number.Digits[i];
                }
                if (number.HasNonZeroTail)
                {
                    digits[digitCount] = '1';
                }

                ulong bits = ConvertDecimalToFloatingBits<TFloat>(digits, length, (long)number.Scale - length, negative: false);
                result = TFloat.BitsToFloat(bits);
            }

            return number.IsNegative ? -result : result;
        }

        internal static bool SpanStartsWith<TChar>(ReadOnlySpan<TChar> span, TChar c)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            return !span.IsEmpty && span[0].Equals(c);
        }

        internal static bool SpanStartsWith<TChar>(ReadOnlySpan<TChar> span, ReadOnlySpan<TChar> value, StringComparison comparisonType)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (typeof(TChar) == typeof(char))
            {
                ReadOnlySpan<char> typedSpan = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<char>>(span);
                ReadOnlySpan<char> typedValue = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<char>>(value);
                return typedSpan.StartsWith(typedValue, comparisonType);
            }

            ReadOnlySpan<byte> bytes = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<byte>>(span);
            ReadOnlySpan<byte> prefix = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<byte>>(value);
            return Encoding.UTF8.GetString(bytes).AsSpan().StartsWith(Encoding.UTF8.GetString(prefix), comparisonType);
        }

        internal static bool SpanEqualsOrdinalIgnoreCase<TChar>(ReadOnlySpan<TChar> span, ReadOnlySpan<TChar> value)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (typeof(TChar) == typeof(char))
            {
                ReadOnlySpan<char> typedSpan = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<char>>(span);
                ReadOnlySpan<char> typedValue = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<char>>(value);
                return typedSpan.Equals(typedValue, StringComparison.OrdinalIgnoreCase);
            }

            ReadOnlySpan<byte> left = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<byte>>(span);
            ReadOnlySpan<byte> right = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<byte>>(value);
            return Encoding.UTF8.GetString(left).AsSpan().Equals(Encoding.UTF8.GetString(right), StringComparison.OrdinalIgnoreCase);
        }

        internal static ReadOnlySpan<TChar> SpanTrimStart<TChar>(ReadOnlySpan<TChar> span)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (typeof(TChar) == typeof(char))
            {
                return Unsafe.BitCast<ReadOnlySpan<char>, ReadOnlySpan<TChar>>(Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<char>>(span).TrimStart());
            }

            // UTF-8: skip the same leading white space the UTF-16 form trims.
            ReadOnlySpan<byte> bytes = Unsafe.BitCast<ReadOnlySpan<TChar>, ReadOnlySpan<byte>>(span);
            string text = Encoding.UTF8.GetString(bytes);
            int trimmed = text.Length - text.AsSpan().TrimStart().Length;
            return span.Slice(Encoding.UTF8.GetByteCount(text.AsSpan(0, trimmed)));
        }

        // Matches a special-value symbol (Infinity/NaN and signed variants) as a prefix of candidate,
        // which must be a suffix of value (i.e. value with any leading whitespace and sign removed).
        // elementsConsumed must be set by the caller to the offset of candidate within value; on success
        // it is advanced by the number of elements consumed here (the symbol plus any trailing whitespace),
        // and on failure it is left unchanged. Leading and trailing whitespace around a special value is
        // always consumed, independent of the AllowLeadingWhite/AllowTrailingWhite styles (historical
        // precedent). When a non-whitespace character remains after the trailing whitespace, the match is
        // only accepted when AllowTrailingInvalidCharacters is set, in which case parsing stops at that
        // character.
        internal static bool TryMatchSpecialValueSymbol<TChar>(ReadOnlySpan<TChar> candidate, ReadOnlySpan<TChar> symbol, bool allowTrailingInvalid, ref int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (!symbol.IsEmpty && symbol.Length <= candidate.Length && SpanEqualsOrdinalIgnoreCase(candidate.Slice(0, symbol.Length), symbol))
            {
                ReadOnlySpan<TChar> trailing = SpanTrimStart(candidate.Slice(symbol.Length));

                if (trailing.IsEmpty || allowTrailingInvalid)
                {
                    elementsConsumed += candidate.Length - trailing.Length;
                    return true;
                }
            }

            return false;
        }
    }
}
