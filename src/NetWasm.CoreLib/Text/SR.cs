// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Text
{
    internal static class SR
    {
        internal const string ArgumentOutOfRange_Range =
            "Valid values are between {0} and {1}, inclusive.";
        internal const string ArgumentOutOfRange_GetByteCountOverflow =
            "Too many characters. The resulting number of bytes is larger than what can be returned as an int.";
        internal const string ArgumentOutOfRange_IndexCount =
            "Index and count must refer to a location within the string.";
        internal const string ArgumentOutOfRange_IndexCountBuffer =
            "Index and count must refer to a location within the buffer.";
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual =
            "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string Argument_EncodingConversionOverflowBytes =
            "The output byte buffer is too small to contain the encoded data, encoding codepage '{0}' and fallback '{1}'.";
        internal const string Argument_EncodingConversionOverflowChars =
            "The output char buffer is too small to contain the decoded characters, encoding codepage '{0}' and fallback '{1}'.";
        internal const string Argument_InvalidCharSequenceNoIndex =
            "String contains invalid Unicode code points.";

        internal const string Arg_EmptySpan =
            "Span may not be empty.";
        internal const string Arg_LongerThanSrcString =
            "Source string was not long enough. Check sourceIndex and count.";
        internal const string ArgumentOutOfRange_Capacity =
            "Capacity exceeds maximum capacity.";
        internal const string ArgumentOutOfRange_IndexLength =
            "Index and length must refer to a location within the string.";
        internal const string ArgumentOutOfRange_LengthGreaterThanCapacity =
            "The length cannot be greater than the capacity.";
        internal const string ArgumentOutOfRange_OffsetOut =
            "Either offset did not refer to a position in the string, or there is an insufficient length of destination character array.";
        internal const string ArgumentOutOfRange_SmallCapacity =
            "capacity was less than the current size.";
        internal const string ArgumentOutOfRange_StartIndexLargerThanLength =
            "startIndex cannot be larger than length of string.";
        internal const string InvalidOperation_ConcurrentOperationsNotSupported =
            "Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.";
        internal const string Serialization_StringBuilderCapacity =
            "The serialized Capacity property of StringBuilder must be positive, less than or equal to MaxCapacity and greater than or equal to the String length.";
        internal const string Serialization_StringBuilderMaxCapacity =
            "The serialized MaxCapacity property of StringBuilder must be positive and greater than or equal to the String length.";

        internal const string Arg_ArgumentException =
            "Value does not fall within the expected range.";
        internal const string Arg_MustBeRune =
            "Object must be of type Rune.";
        internal const string ArgumentOutOfRange_GetCharCountOverflow =
            "Too many bytes. The resulting number of chars is larger than what can be returned as an int.";
        internal const string ArgumentOutOfRange_NeedNonNegNum =
            "Non-negative number required.";
        internal const string Argument_CodepageNotSupported =
            "{0} is not a supported code page.";
        internal const string Argument_ConversionOverflow =
            "Conversion buffer overflow.";
        internal const string Argument_EncoderFallbackNotEmpty =
            "Must complete Convert() operation or call Encoder.Reset() before calling GetBytes() or GetByteCount(). Encoder '{0}' fallback '{1}'.";
        internal const string Argument_EncodingNotSupported =
            "'{0}' is not a supported encoding name. For information on defining a custom encoding, see the documentation for the Encoding.RegisterProvider method.";
        internal const string Argument_FallbackBufferNotEmpty =
            "Cannot change fallback when buffer is not empty. Previous Convert() call left data in the fallback buffer.";
        internal const string Argument_InvalidCodePageBytesIndex =
            "Unable to translate bytes {0} at index {1} from specified code page to Unicode.";
        internal const string Argument_InvalidCodePageConversionIndex =
            "Unable to translate Unicode character \\\\u{0:X4} at index {1} to specified code page.";
        internal const string Argument_RecursiveFallback =
            "Recursive fallback not allowed for character \\\\u{0:X4}.";
        internal const string Argument_RecursiveFallbackBytes =
            "Recursive fallback not allowed for bytes {0}.";
        internal const string Encoding_UTF7_Disabled =
            "Support for UTF-7 is disabled. See {0} for more information.";
        internal const string Globalization_cp_1200 =
            "Unicode";
        internal const string Globalization_cp_12000 =
            "Unicode (UTF-32)";
        internal const string Globalization_cp_12001 =
            "Unicode (UTF-32 Big-Endian)";
        internal const string Globalization_cp_1201 =
            "Unicode (Big-Endian)";
        internal const string Globalization_cp_20127 =
            "US-ASCII";
        internal const string Globalization_cp_28591 =
            "Western European (ISO)";
        internal const string Globalization_cp_65000 =
            "Unicode (UTF-7)";
        internal const string Globalization_cp_65001 =
            "Unicode (UTF-8)";
        internal const string InvalidOperation_ReadOnly =
            "Instance is read-only.";
        internal const string NotSupported_NoCodepageData =
            "No data is available for encoding {0}. For information on defining a custom encoding, see the documentation for the Encoding.RegisterProvider method.";
        internal const string NotSupported_UnseekableStream =
            "Stream does not support seeking.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
