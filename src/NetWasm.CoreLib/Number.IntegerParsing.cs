// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce, System.Private.CoreLib
// Number.Parsing.cs (TryNumberBufferToBinaryInteger through TryParseBinaryIntegerHexOrBinaryNumberStyle,
// and ThrowOverflowOrFormatException/ThrowOverflowException).
// NetWasm: the throw helpers raise FormatException and OverflowException without upstream's
// value-bearing messages, as NetWasm's other parsers do.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    internal static partial class Number
    {
        private static unsafe bool TryNumberBufferToBinaryInteger<TInteger>(ref NumberBuffer number, ref TInteger value)
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            number.CheckConsistency();

            int i = number.Scale;

            if ((i > TInteger.MaxDigitCount) || (i < number.DigitsCount) || (!TInteger.IsSigned && number.IsNegative) || number.HasNonZeroTail)
            {
                return false;
            }

            byte* p = number.DigitsPtr;

            Debug.Assert(p != null);
            TInteger n = TInteger.Zero;

            while (--i >= 0)
            {
                if (TInteger.IsGreaterThanAsUnsigned(n, TInteger.MaxValueDiv10))
                {
                    return false;
                }

                n = TInteger.MultiplyBy10(n);

                if (*p != '\0')
                {
                    TInteger newN = n + TInteger.CreateTruncating(*p++ - '0');

                    if (!TInteger.IsSigned && (newN < n))
                    {
                        return false;
                    }

                    n = newN;
                }
            }

            if (TInteger.IsSigned)
            {
                if (number.IsNegative)
                {
                    n = -n;

                    if (n > TInteger.Zero)
                    {
                        return false;
                    }
                }
                else if (n < TInteger.Zero)
                {
                    return false;
                }
            }

            value = n;
            return true;
        }

        internal static TInteger ParseBinaryInteger<TChar, TInteger>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info)
            where TChar : unmanaged, IUtfChar<TChar>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            ParsingStatus status = TryParseBinaryInteger(value, styles, info, out TInteger result, out _);

            if (status != ParsingStatus.OK)
            {
                ThrowOverflowOrFormatException<TChar, TInteger>(status, value);
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ParsingStatus TryParseBinaryInteger<TChar, TInteger>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info, out TInteger result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            if ((styles & ~(NumberStyles.Integer | AllowTrailingInvalidCharacters)) == 0)
            {
                // Optimized path for the common case of anything that's allowed for integer style.
                return TryParseBinaryIntegerStyle(value, styles, info, out result, out elementsConsumed);
            }

            if ((styles & NumberStyles.AllowHexSpecifier) != 0)
            {
                return TryParseBinaryIntegerHexOrBinaryNumberStyle<TChar, TInteger, HexParser<TInteger>>(value, styles, out result, out elementsConsumed);
            }

            if ((styles & NumberStyles.AllowBinarySpecifier) != 0)
            {
                return TryParseBinaryIntegerHexOrBinaryNumberStyle<TChar, TInteger, BinaryParser<TInteger>>(value, styles, out result, out elementsConsumed);
            }

            return TryParseBinaryIntegerNumber(value, styles, info, out result, out elementsConsumed);
        }

        private static ParsingStatus TryParseBinaryIntegerNumber<TChar, TInteger>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info, out TInteger result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            result = TInteger.Zero;
            NumberBuffer number = new NumberBuffer(NumberBufferKind.Integer, stackalloc byte[TInteger.MaxDigitCount + 1]);

            if (!TryStringToNumber(value, styles, ref number, info, out elementsConsumed))
            {
                return ParsingStatus.Failed;
            }

            if (!TryNumberBufferToBinaryInteger(ref number, ref result))
            {
                elementsConsumed = 0;
                return ParsingStatus.Overflow;
            }

            return ParsingStatus.OK;
        }

        /// <summary>Parses int limited to styles that make up NumberStyles.Integer.</summary>
        internal static ParsingStatus TryParseBinaryIntegerStyle<TChar, TInteger>(ReadOnlySpan<TChar> value, NumberStyles styles, NumberFormatInfo info, out TInteger result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            Debug.Assert((styles & ~(NumberStyles.Integer | AllowTrailingInvalidCharacters)) == 0, "Only handles subsets of Integer format");

            if (value.IsEmpty)
            {
                goto FalseExit;
            }

            int index = 0;
            uint num = TChar.CastToUInt32(value[0]);

            // Skip past any whitespace at the beginning.
            if ((styles & NumberStyles.AllowLeadingWhite) != 0 && IsWhite(num))
            {
                do
                {
                    index++;

                    if ((uint)index >= (uint)value.Length)
                    {
                        goto FalseExit;
                    }
                    num = TChar.CastToUInt32(value[index]);
                }
                while (IsWhite(num));
            }

            // Parse leading sign.
            bool isNegative = false;
            if ((styles & NumberStyles.AllowLeadingSign) != 0)
            {
                if (info.HasInvariantNumberSigns)
                {
                    if (num == '-')
                    {
                        isNegative = true;
                        index++;

                        if ((uint)index >= (uint)value.Length)
                        {
                            goto FalseExit;
                        }
                        num = TChar.CastToUInt32(value[index]);
                    }
                    else if (num == '+')
                    {
                        index++;

                        if ((uint)index >= (uint)value.Length)
                        {
                            goto FalseExit;
                        }
                        num = TChar.CastToUInt32(value[index]);
                    }
                }
                else
                {
                    int nextIndex = MatchSignChars(value, index, info, out isNegative);
                    if (nextIndex >= 0)
                    {
                        index = nextIndex;

                        if ((uint)index >= (uint)value.Length)
                        {
                            goto FalseExit;
                        }
                        num = TChar.CastToUInt32(value[index]);
                    }
                }
            }

            bool overflow = !TInteger.IsSigned && isNegative;
            TInteger answer = TInteger.Zero;

            if (IsDigit(num))
            {
                // Skip past leading zeros.
                if (num == '0')
                {
                    do
                    {
                        index++;

                        if ((uint)index >= (uint)value.Length)
                        {
                            goto DoneAtEnd;
                        }
                        num = TChar.CastToUInt32(value[index]);
                    } while (num == '0');

                    if (!IsDigit(num))
                    {
                        if (!TInteger.IsSigned)
                        {
                            overflow = false;
                        }
                        goto HasTrailingChars;
                    }
                }

                // Parse most digits, up to the potential for overflow, which can't happen until after MaxDigitCount - 1 digits.
                answer = TInteger.CreateTruncating(num - '0'); // first digit
                index++;

                for (int i = 0; i < TInteger.MaxDigitCount - 2; i++) // next MaxDigitCount - 2 digits can't overflow
                {
                    if ((uint)index >= (uint)value.Length)
                    {
                        if (!TInteger.IsSigned)
                        {
                            goto DoneAtEndButPotentialOverflow;
                        }
                        else
                        {
                            goto DoneAtEnd;
                        }
                    }

                    num = TChar.CastToUInt32(value[index]);

                    if (!IsDigit(num))
                    {
                        goto HasTrailingChars;
                    }
                    index++;

                    answer = TInteger.MultiplyBy10(answer);
                    answer += TInteger.CreateTruncating(num - '0');
                }

                if ((uint)index >= (uint)value.Length)
                {
                    if (!TInteger.IsSigned)
                    {
                        goto DoneAtEndButPotentialOverflow;
                    }
                    else
                    {
                        goto DoneAtEnd;
                    }
                }

                num = TChar.CastToUInt32(value[index]);

                if (!IsDigit(num))
                {
                    goto HasTrailingChars;
                }
                index++;

                // Potential overflow now processing the MaxDigitCount digit.
                if (!TInteger.IsSigned)
                {
                    overflow |= (answer > TInteger.MaxValueDiv10) || ((answer == TInteger.MaxValueDiv10) && (num > '5'));
                }
                else
                {
                    overflow = answer > TInteger.MaxValueDiv10;
                }

                answer = TInteger.MultiplyBy10(answer);
                answer += TInteger.CreateTruncating(num - '0');

                if (TInteger.IsSigned)
                {
                    overflow |= TInteger.IsGreaterThanAsUnsigned(answer, TInteger.MaxValue + (isNegative ? TInteger.One : TInteger.Zero));
                }

                if ((uint)index >= (uint)value.Length)
                {
                    goto DoneAtEndButPotentialOverflow;
                }

                // At this point, we're either overflowing or hitting a formatting error.
                // Format errors take precedence for compatibility.
                num = TChar.CastToUInt32(value[index]);

                while (IsDigit(num))
                {
                    overflow = true;
                    index++;

                    if ((uint)index >= (uint)value.Length)
                    {
                        goto OverflowExit;
                    }
                    num = TChar.CastToUInt32(value[index]);
                }
                goto HasTrailingChars;
            }
            goto FalseExit;

        DoneAtEndButPotentialOverflow:
            if (overflow)
            {
                goto OverflowExit;
            }

        DoneAtEnd:
            if (!TInteger.IsSigned)
            {
                result = answer;
            }
            else
            {
                result = isNegative ? -answer : answer;
            }
            ParsingStatus status = ParsingStatus.OK;
            elementsConsumed = index;

        Exit:
            return status;

        InvalidExit:
            // For compatibility we still need to process any trailing
            // nulls that exist and report them as having been consumed.

            index = ConsumeTrailingNulls(value, index);

            if ((index == value.Length) || ((styles & AllowTrailingInvalidCharacters) != 0))
            {
                goto DoneAtEndButPotentialOverflow;
            }

        FalseExit: // parsing failed
            result = TInteger.Zero;
            elementsConsumed = 0;
            status = ParsingStatus.Failed;
            goto Exit;

        OverflowExit:
            result = TInteger.Zero;
            elementsConsumed = 0;
            status = ParsingStatus.Overflow;
            goto Exit;

        HasTrailingChars: // we've successfully parsed, but there are still remaining characters in the span
            // Skip past trailing whitespace, then past trailing zeros, and if anything else remains, fail.
            if (IsWhite(num))
            {
                if ((styles & NumberStyles.AllowTrailingWhite) == 0)
                {
                    goto InvalidExit;
                }

                for (index++; index < value.Length; index++)
                {
                    uint ch = TChar.CastToUInt32(value[index]);

                    if (!IsWhite(ch))
                    {
                        break;
                    }
                }
                if ((uint)index >= (uint)value.Length)
                    goto DoneAtEndButPotentialOverflow;
            }
            goto InvalidExit;
        }

        internal interface IHexOrBinaryParser<TInteger>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            static abstract NumberStyles AllowedStyles { get; }
            static abstract bool IsValidChar(uint ch);
            static abstract uint FromChar(uint ch);
            static abstract uint MaxDigitValue { get; }
            static abstract int MaxDigitCount { get; }
            static abstract TInteger ShiftLeftForNextDigit(TInteger value);
        }

        internal readonly struct HexParser<TInteger> : IHexOrBinaryParser<TInteger> where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            public static NumberStyles AllowedStyles => NumberStyles.HexNumber | AllowTrailingInvalidCharacters;
            public static bool IsValidChar(uint ch) => HexConverter.IsHexChar((int)ch);
            public static uint FromChar(uint ch) => (uint)HexConverter.FromChar((int)ch);
            public static uint MaxDigitValue => 0xF;
            public static int MaxDigitCount => TInteger.MaxHexDigitCount;
            public static TInteger ShiftLeftForNextDigit(TInteger value) => TInteger.MultiplyBy16(value);
        }

        private readonly struct BinaryParser<TInteger> : IHexOrBinaryParser<TInteger> where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            public static NumberStyles AllowedStyles => NumberStyles.BinaryNumber | AllowTrailingInvalidCharacters;
            public static bool IsValidChar(uint ch) => (ch - '0') <= 1;
            public static uint FromChar(uint ch) => ch - '0';
            public static uint MaxDigitValue => 1;
            public static unsafe int MaxDigitCount => sizeof(TInteger) * 8;
            public static TInteger ShiftLeftForNextDigit(TInteger value) => value << 1;
        }

        internal static ParsingStatus TryParseBinaryIntegerHexOrBinaryNumberStyle<TChar, TInteger, TParser>(ReadOnlySpan<TChar> value, NumberStyles styles, out TInteger result, out int elementsConsumed)
            where TChar : unmanaged, IUtfChar<TChar>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
            where TParser : struct, IHexOrBinaryParser<TInteger>
        {
            Debug.Assert((styles & ~TParser.AllowedStyles) == 0, $"Only handles subsets of {TParser.AllowedStyles} format");

            if (value.IsEmpty)
            {
                goto FalseExit;
            }

            int index = 0;
            uint num = TChar.CastToUInt32(value[0]);

            // Skip past any whitespace at the beginning.
            if ((styles & NumberStyles.AllowLeadingWhite) != 0 && IsWhite(num))
            {
                do
                {
                    index++;

                    if ((uint)index >= (uint)value.Length)
                    {
                        goto FalseExit;
                    }
                    num = TChar.CastToUInt32(value[index]);
                }
                while (IsWhite(num));
            }

            bool overflow = false;
            TInteger answer = TInteger.Zero;

            if (TParser.IsValidChar(num))
            {
                // Skip past leading zeros.
                if (num == '0')
                {
                    do
                    {
                        index++;

                        if ((uint)index >= (uint)value.Length)
                        {
                            goto DoneAtEnd;
                        }
                        num = TChar.CastToUInt32(value[index]);
                    } while (num == '0');

                    if (!TParser.IsValidChar(num))
                    {
                        goto HasTrailingChars;
                    }
                }

                // Parse up through MaxDigitCount digits, as no overflow is possible
                answer = TInteger.CreateTruncating(TParser.FromChar(num)); // first digit
                index++;

                for (int i = 0; i < TParser.MaxDigitCount - 1; i++) // next MaxDigitCount - 1 digits can't overflow
                {
                    if ((uint)index >= (uint)value.Length)
                    {
                        goto DoneAtEnd;
                    }
                    num = TChar.CastToUInt32(value[index]);

                    uint numValue = TParser.FromChar(num);

                    if (numValue > TParser.MaxDigitValue)
                    {
                        goto HasTrailingChars;
                    }
                    index++;

                    answer = TParser.ShiftLeftForNextDigit(answer);
                    answer += TInteger.CreateTruncating(numValue);
                }

                // If there's another digit, it's an overflow.
                if ((uint)index >= (uint)value.Length)
                {
                    goto DoneAtEnd;
                }

                num = TChar.CastToUInt32(value[index]);

                if (!TParser.IsValidChar(num))
                {
                    goto HasTrailingChars;
                }

                // At this point, we're either overflowing or hitting a formatting error.
                // Format errors take precedence for compatibility. Read through any remaining digits.
                do
                {
                    index++;

                    if ((uint)index >= (uint)value.Length)
                    {
                        goto OverflowExit;
                    }
                    num = TChar.CastToUInt32(value[index]);
                } while (TParser.IsValidChar(num));

                overflow = true;
                goto HasTrailingChars;
            }
            goto FalseExit;

        DoneAtEndButPotentialOverflow:
            if (overflow)
            {
                goto OverflowExit;
            }

        DoneAtEnd:
            result = answer;
            ParsingStatus status = ParsingStatus.OK;
            elementsConsumed = index;

        Exit:
            return status;

        InvalidExit:
            // For compatibility we still need to process any trailing
            // nulls that exist and report them as having been consumed.

            index = ConsumeTrailingNulls(value, index);

            if ((index == value.Length) || ((styles & AllowTrailingInvalidCharacters) != 0))
            {
                goto DoneAtEndButPotentialOverflow;
            }

        FalseExit: // parsing failed
            result = TInteger.Zero;
            elementsConsumed = 0;
            status = ParsingStatus.Failed;
            goto Exit;

        OverflowExit:
            result = TInteger.Zero;
            elementsConsumed = 0;
            status = ParsingStatus.Overflow;
            goto Exit;

        HasTrailingChars: // we've successfully parsed, but there are still remaining characters in the span
            // Skip past trailing whitespace, then past trailing zeros, and if anything else remains, fail.
            if (IsWhite(num))
            {
                if ((styles & NumberStyles.AllowTrailingWhite) == 0)
                {
                    goto InvalidExit;
                }

                for (index++; index < value.Length; index++)
                {
                    uint ch = TChar.CastToUInt32(value[index]);

                    if (!IsWhite(ch))
                    {
                        break;
                    }
                }

                if ((uint)index >= (uint)value.Length)
                {
                    goto DoneAtEndButPotentialOverflow;
                }
            }
            goto InvalidExit;
        }


        [DoesNotReturn]
        internal static void ThrowOverflowOrFormatException<TChar, TInteger>(ParsingStatus status, ReadOnlySpan<TChar> value)
            where TChar : unmanaged, IUtfChar<TChar>
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            if (status == ParsingStatus.Failed)
            {
                throw new FormatException();
            }
            ThrowOverflowException<TInteger>();
        }

        [DoesNotReturn]
        internal static void ThrowOverflowException<TInteger>()
            where TInteger : unmanaged, IBinaryIntegerParseAndFormatInfo<TInteger>
        {
            throw new OverflowException(TInteger.OverflowMessage);
        }
    }
}
