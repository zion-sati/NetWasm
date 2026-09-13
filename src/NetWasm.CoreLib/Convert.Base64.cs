// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Buffers.Text;

namespace System
{
    // Base64 and hexadecimal conversion members are derived from the .NET
    // System.Convert implementation. The surrounding conversion matrix is not
    // part of the supported NetWasm CoreLib profile.
    public static partial class Convert
    {
        private const int Base64LineBreakPosition = 76;

        public static string ToBase64String(byte[] inArray)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            return Base64.EncodeToString(new ReadOnlySpan<byte>(inArray));
        }

        public static string ToBase64String(byte[] inArray, Base64FormattingOptions options)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            return ToBase64String(new ReadOnlySpan<byte>(inArray), options);
        }

        public static string ToBase64String(byte[] inArray, int offset, int length)
        {
            return ToBase64String(inArray, offset, length, Base64FormattingOptions.None);
        }

        public static string ToBase64String(byte[] inArray, int offset, int length, Base64FormattingOptions options)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            ValidateArrayRange(inArray.Length, offset, length);
            return ToBase64String(new ReadOnlySpan<byte>(inArray, offset, length), options);
        }

        public static string ToBase64String(
            ReadOnlySpan<byte> bytes,
            Base64FormattingOptions options = System.Base64FormattingOptions.None)
        {
            ValidateFormattingOptions(options);

            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            if (options == Base64FormattingOptions.None)
            {
                return Base64.EncodeToString(bytes);
            }

            int outputLength = ToBase64CalculateAndValidateOutputLength(bytes.Length, insertLineBreaks: true);
            char[] buffer = new char[outputLength];
            int charsWritten = ConvertToBase64WithLineBreaks(new Span<char>(buffer), bytes);
            if (charsWritten != buffer.Length)
            {
                throw new InvalidOperationException();
            }
            return string.Create(buffer);
        }

        public static int ToBase64CharArray(byte[] inArray, int offsetIn, int length, char[] outArray, int offsetOut)
        {
            return ToBase64CharArray(inArray, offsetIn, length, outArray, offsetOut, Base64FormattingOptions.None);
        }

        public static int ToBase64CharArray(byte[] inArray, int offsetIn, int length, char[] outArray, int offsetOut, Base64FormattingOptions options)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            ArgumentNullException.ThrowIfNull(outArray);
            ValidateArrayRange(inArray.Length, offsetIn, length);
            if (offsetOut < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offsetOut));
            }
            ValidateFormattingOptions(options);

            if (length == 0)
            {
                return 0;
            }

            bool insertLineBreaks = options == Base64FormattingOptions.InsertLineBreaks;
            int charLengthRequired = ToBase64CalculateAndValidateOutputLength(length, insertLineBreaks);
            if (offsetOut > outArray.Length - charLengthRequired)
            {
                throw new ArgumentOutOfRangeException(nameof(offsetOut));
            }

            Span<char> destination = new Span<char>(outArray, offsetOut, charLengthRequired);
            ReadOnlySpan<byte> source = new ReadOnlySpan<byte>(inArray, offsetIn, length);
            if (insertLineBreaks)
            {
                ConvertToBase64WithLineBreaks(destination, source);
            }
            else
            {
                Base64.EncodeToChars(source, destination, out _, out _);
            }
            return charLengthRequired;
        }

        public static bool TryToBase64Chars(
            ReadOnlySpan<byte> bytes,
            Span<char> chars,
            out int charsWritten,
            Base64FormattingOptions options = System.Base64FormattingOptions.None)
        {
            ValidateFormattingOptions(options);

            if (bytes.Length == 0)
            {
                charsWritten = 0;
                return true;
            }

            bool insertLineBreaks = options == Base64FormattingOptions.InsertLineBreaks;
            int charLengthRequired = ToBase64CalculateAndValidateOutputLength(bytes.Length, insertLineBreaks);
            if (charLengthRequired > chars.Length)
            {
                charsWritten = 0;
                return false;
            }

            if (insertLineBreaks)
            {
                ConvertToBase64WithLineBreaks(chars, bytes);
            }
            else
            {
                Base64.EncodeToChars(bytes, chars, out _, out _);
            }

            charsWritten = charLengthRequired;
            return true;
        }

        private static int ConvertToBase64WithLineBreaks(Span<char> destination, ReadOnlySpan<byte> source)
        {
            int writeOffset = 0;
            while (true)
            {
                int chunkSize = Math.Min(source.Length, Base64LineBreakPosition / 4 * 3);
                Base64.EncodeToChars(source.Slice(0, chunkSize), destination.Slice(writeOffset), out _, out int charsWritten);
                source = source.Slice(chunkSize);
                writeOffset += charsWritten;
                if (source.IsEmpty)
                {
                    break;
                }
                destination[writeOffset++] = '\r';
                destination[writeOffset++] = '\n';
            }
            return writeOffset;
        }

        private static int ToBase64CalculateAndValidateOutputLength(int inputLength, bool insertLineBreaks)
        {
            uint outputLength = ((uint)inputLength + 2) / 3 * 4;
            if (outputLength == 0)
            {
                return 0;
            }
            if (insertLineBreaks)
            {
                uint newLines = outputLength / Base64LineBreakPosition;
                uint remainder = outputLength % Base64LineBreakPosition;
                if (remainder == 0)
                {
                    --newLines;
                }
                outputLength += newLines * 2;
            }
            if (outputLength > int.MaxValue)
            {
                throw new OutOfMemoryException();
            }
            return (int)outputLength;
        }

        public static byte[] FromBase64String(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Base64.DecodeFromChars(s);
        }

        public static bool TryFromBase64String(string s, Span<byte> bytes, out int bytesWritten)
        {
            ArgumentNullException.ThrowIfNull(s);
            return TryFromBase64Chars(s.AsSpan(), bytes, out bytesWritten);
        }

        public static bool TryFromBase64Chars(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten)
        {
            OperationStatus status = Base64.DecodeFromChars(chars, bytes, out _, out bytesWritten);
            if (status == OperationStatus.Done)
            {
                return true;
            }
            bytesWritten = 0;
            return false;
        }

        public static byte[] FromBase64CharArray(char[] inArray, int offset, int length)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            ValidateArrayRange(inArray.Length, offset, length);
            return Base64.DecodeFromChars(new ReadOnlySpan<char>(inArray, offset, length));
        }

        public static byte[] FromHexString(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return FromHexString(s.AsSpan());
        }

        public static byte[] FromHexString(ReadOnlySpan<char> chars)
        {
            if (chars.Length == 0)
            {
                return [];
            }
            if ((chars.Length & 1) != 0)
            {
                throw new FormatException("The input is not a valid hexadecimal string.");
            }
            byte[] result = new byte[chars.Length / 2];
            if (!HexConverter.TryDecodeFromUtf16(chars, result, out _))
            {
                throw new FormatException("The input is not a valid hexadecimal string.");
            }
            return result;
        }

        public static byte[] FromHexString(ReadOnlySpan<byte> utf8Source)
        {
            if (utf8Source.Length == 0)
            {
                return [];
            }
            if ((utf8Source.Length & 1) != 0)
            {
                throw new FormatException("The input is not a valid hexadecimal string.");
            }
            byte[] result = new byte[utf8Source.Length / 2];
            if (!HexConverter.TryDecodeFromUtf8(utf8Source, result, out _))
            {
                throw new FormatException("The input is not a valid hexadecimal string.");
            }
            return result;
        }

        public static OperationStatus FromHexString(string source, Span<byte> destination, out int charsConsumed, out int bytesWritten)
        {
            ArgumentNullException.ThrowIfNull(source);
            return FromHexString(source.AsSpan(), destination, out charsConsumed, out bytesWritten);
        }

        public static OperationStatus FromHexString(ReadOnlySpan<char> source, Span<byte> destination, out int charsConsumed, out int bytesWritten)
        {
            int quotient = source.Length / 2;
            int remainder = source.Length & 1;
            if (quotient == 0)
            {
                charsConsumed = 0;
                bytesWritten = 0;
                return remainder == 1 ? OperationStatus.NeedMoreData : OperationStatus.Done;
            }

            OperationStatus result;
            if (destination.Length < quotient)
            {
                source = source.Slice(0, destination.Length * 2);
                quotient = destination.Length;
                result = OperationStatus.DestinationTooSmall;
            }
            else if (remainder == 1)
            {
                source = source.Slice(0, source.Length - 1);
                result = OperationStatus.NeedMoreData;
            }
            else
            {
                result = OperationStatus.Done;
            }

            destination = destination.Slice(0, quotient);
            if (!HexConverter.TryDecodeFromUtf16(source, destination, out charsConsumed))
            {
                bytesWritten = charsConsumed / 2;
                return OperationStatus.InvalidData;
            }
            bytesWritten = quotient;
            charsConsumed = source.Length;
            return result;
        }

        public static OperationStatus FromHexString(ReadOnlySpan<byte> utf8Source, Span<byte> destination, out int bytesConsumed, out int bytesWritten)
        {
            int quotient = utf8Source.Length / 2;
            int remainder = utf8Source.Length & 1;
            if (quotient == 0)
            {
                bytesConsumed = 0;
                bytesWritten = 0;
                return remainder == 1 ? OperationStatus.NeedMoreData : OperationStatus.Done;
            }

            OperationStatus result;
            if (destination.Length < quotient)
            {
                utf8Source = utf8Source.Slice(0, destination.Length * 2);
                quotient = destination.Length;
                result = OperationStatus.DestinationTooSmall;
            }
            else if (remainder == 1)
            {
                utf8Source = utf8Source.Slice(0, utf8Source.Length - 1);
                result = OperationStatus.NeedMoreData;
            }
            else
            {
                result = OperationStatus.Done;
            }

            destination = destination.Slice(0, quotient);
            if (!HexConverter.TryDecodeFromUtf8(utf8Source, destination, out bytesConsumed))
            {
                bytesWritten = bytesConsumed / 2;
                return OperationStatus.InvalidData;
            }
            bytesWritten = quotient;
            bytesConsumed = utf8Source.Length;
            return result;
        }

        public static string ToHexString(byte[] inArray)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            return ToHexString(new ReadOnlySpan<byte>(inArray));
        }

        public static string ToHexString(byte[] inArray, int offset, int length)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            ValidateArrayRange(inArray.Length, offset, length);
            return ToHexString(new ReadOnlySpan<byte>(inArray, offset, length));
        }

        public static string ToHexString(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }
            if (bytes.Length > int.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }
            return HexConverter.ToString(bytes, HexConverter.Casing.Upper);
        }

        public static bool TryToHexString(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
        {
            if (source.Length == 0)
            {
                charsWritten = 0;
                return true;
            }
            if (source.Length > int.MaxValue / 2 || destination.Length < source.Length * 2)
            {
                charsWritten = 0;
                return false;
            }
            HexConverter.EncodeToUtf16(source, destination);
            charsWritten = source.Length * 2;
            return true;
        }

        public static bool TryToHexString(ReadOnlySpan<byte> source, Span<byte> utf8Destination, out int bytesWritten)
        {
            if (source.Length == 0)
            {
                bytesWritten = 0;
                return true;
            }
            if (source.Length > int.MaxValue / 2 || utf8Destination.Length < source.Length * 2)
            {
                bytesWritten = 0;
                return false;
            }
            HexConverter.EncodeToUtf8(source, utf8Destination);
            bytesWritten = source.Length * 2;
            return true;
        }

        public static string ToHexStringLower(byte[] inArray)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            return ToHexStringLower(new ReadOnlySpan<byte>(inArray));
        }

        public static string ToHexStringLower(byte[] inArray, int offset, int length)
        {
            ArgumentNullException.ThrowIfNull(inArray);
            ValidateArrayRange(inArray.Length, offset, length);
            return ToHexStringLower(new ReadOnlySpan<byte>(inArray, offset, length));
        }

        public static string ToHexStringLower(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }
            if (bytes.Length > int.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }
            return HexConverter.ToString(bytes, HexConverter.Casing.Lower);
        }

        public static bool TryToHexStringLower(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten)
        {
            if (source.Length == 0)
            {
                charsWritten = 0;
                return true;
            }
            if (source.Length > int.MaxValue / 2 || destination.Length < source.Length * 2)
            {
                charsWritten = 0;
                return false;
            }
            HexConverter.EncodeToUtf16(source, destination, HexConverter.Casing.Lower);
            charsWritten = source.Length * 2;
            return true;
        }

        public static bool TryToHexStringLower(ReadOnlySpan<byte> source, Span<byte> utf8Destination, out int bytesWritten)
        {
            if (source.Length == 0)
            {
                bytesWritten = 0;
                return true;
            }
            if (source.Length > int.MaxValue / 2 || utf8Destination.Length < source.Length * 2)
            {
                bytesWritten = 0;
                return false;
            }
            HexConverter.EncodeToUtf8(source, utf8Destination, HexConverter.Casing.Lower);
            bytesWritten = source.Length * 2;
            return true;
        }

        private static void ValidateFormattingOptions(Base64FormattingOptions options)
        {
            if ((uint)options > (uint)Base64FormattingOptions.InsertLineBreaks)
            {
                throw new ArgumentException("The value is not a valid Base64FormattingOptions value.", nameof(options));
            }
        }

        private static void ValidateArrayRange(int arrayLength, int offset, int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (offset > arrayLength - length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }
    }
}
