// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptations: NetWasm's Encoding, Encoder and Decoder have no EncoderNLS/DecoderNLS,
// EncodingByteBuffer/EncodingCharBuffer or internal pointer-based coding hooks. The upstream
// GetBytes and GetChars bodies run unchanged over spans with private equivalents of those
// helpers, and the nested Encoder and Decoder carry EncoderNLS/DecoderNLS's validation, MustFlush
// and throw-on-overflow rules for every public overload, including the span overloads that
// NetWasm's base classes implement generically. Decoder fallbacks go through the public
// DecoderFallbackBuffer contract (upstream's internal counting shortcut in
// DecoderUTF7FallbackBuffer gives the same counts, so it is not ported). The default fallbacks
// are passed to the base constructor instead of overriding SetDefaultFallbacks, and EncodingName
// and WebName are overridden because NetWasm's Encoding has no code-page table.

//
// Don't override IsAlwaysNormalized because it is just a Unicode Transformation and could be confused.
//

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Text
{
    public class UTF7Encoding : Encoding
    {
#pragma warning disable SYSLIB0001
        // Used by Encoding.UTF7 for lazy initialization
        // The initialization code will not be run until a static member of the class is referenced
        internal static readonly UTF7Encoding s_default = new UTF7Encoding();
#pragma warning restore SYSLIB0001

        // The set of base 64 characters.
        private byte[] _base64Bytes;
        // The decoded bits for every base64 values. This array has a size of 128 elements.
        // The index is the code point value of the base 64 characters.  The value is -1 if
        // the code point is not a valid base 64 character.  Otherwise, the value is a value
        // from 0 ~ 63.
        private sbyte[] _base64Values;
        // The array to decide if a Unicode code point below 0x80 can be directly encoded in UTF7.
        // This array has a size of 128.
        private bool[] _directEncode;

        private readonly bool _allowOptionals;

        private const int UTF7_CODEPAGE = 65000;

        [Obsolete(Obsoletions.SystemTextEncodingUTF7Message, DiagnosticId = Obsoletions.SystemTextEncodingUTF7DiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public UTF7Encoding()
            : this(false)
        {
        }

        [Obsolete(Obsoletions.SystemTextEncodingUTF7Message, DiagnosticId = Obsoletions.SystemTextEncodingUTF7DiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public UTF7Encoding(bool allowOptionals)
            // UTF7 had an odd decoderFallback behavior, and the Encoder fallback
            // is irrelevant because we encode surrogates individually and never check for unmatched ones
            // (so nothing can fallback during encoding)
            : base(UTF7_CODEPAGE, new EncoderReplacementFallback(string.Empty), new DecoderUTF7Fallback()) // Set the data item.
        {
            // Allowing optionals?
            _allowOptionals = allowOptionals;

            // Make our tables
            MakeTables();
        }

        [MemberNotNull(nameof(_base64Bytes))]
        [MemberNotNull(nameof(_base64Values))]
        [MemberNotNull(nameof(_directEncode))]
        private void MakeTables()
        {
            // Build our tables

            _base64Bytes = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8.ToArray();
            Debug.Assert(_base64Bytes.Length == 64);

            _base64Values = new sbyte[128];
            for (int i = 0; i < 128; i++) _base64Values[i] = -1;
            for (int i = 0; i < 64; i++) _base64Values[_base64Bytes[i]] = (sbyte)i;

            // These are the characters that can be directly encoded in UTF7.
            ReadOnlySpan<byte> directChars = "\t\n\r '(),-./0123456789:?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"u8;
            _directEncode = new bool[128];
            foreach (byte c in directChars)
            {
                _directEncode[c] = true;
            }

            if (_allowOptionals)
            {
                // These are the characters that can be optionally directly encoded in UTF7.
                ReadOnlySpan<byte> optionalChars = "!\"#$%&*;<=>@[]^_`{|}"u8;

                foreach (byte c in optionalChars)
                {
                    _directEncode[c] = true;
                }
            }
        }

        public override string EncodingName => "Unicode (UTF-7)";

        public override string WebName => "utf-7";

        public override bool Equals([NotNullWhen(true)] object? value)
        {
            if (value is UTF7Encoding that)
            {
                return (_allowOptionals == that._allowOptionals) &&
                       (EncoderFallback.Equals(that.EncoderFallback)) &&
                       (DecoderFallback.Equals(that.DecoderFallback));
            }
            return false;
        }

        // Compared to all the other encodings, variations of UTF7 are unlikely

        public override int GetHashCode()
        {
            return this.CodePage + this.EncoderFallback.GetHashCode() + this.DecoderFallback.GetHashCode();
        }

        // The following methods are copied from EncodingNLS.cs.
        // Unfortunately EncodingNLS.cs is internal and we're public, so we have to re-implement them here.
        // These should be kept in sync for the following classes:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding

        // Returns the number of bytes required to encode a range of characters in
        // a character array.
        //
        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding
        // parent method is safe

        public override int GetByteCount(char[] chars, int index, int count)
        {
            // Validate input parameters
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (chars.Length - index < count)
                throw new ArgumentOutOfRangeException(nameof(chars), SR.ArgumentOutOfRange_IndexCountBuffer);

            // If no input, return 0, avoid fixed empty array problem
            if (count == 0)
                return 0;

            return GetByteCountCore(new ReadOnlySpan<char>(chars, index, count), null);
        }

        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding
        // parent method is safe

        public override int GetByteCount(string s)
        {
            // Validate input
            ArgumentNullException.ThrowIfNull(s);

            return GetByteCountCore(s.AsSpan(), null);
        }

        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding

        [CLSCompliant(false)]
        public override unsafe int GetByteCount(char* chars, int count)
        {
            // Validate Parameters
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(count);

            return GetByteCountCore(new ReadOnlySpan<char>(chars, count), null);
        }

        // NetWasm: the span overloads use the same cores as the pointer overloads; upstream's
        // Encoding base class reaches them by pinning the spans.
        public override int GetByteCount(ReadOnlySpan<char> chars) => GetByteCountCore(chars, null);

        // Parent method is safe.
        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding

        public override int GetBytes(string s, int charIndex, int charCount,
                                     byte[] bytes, int byteIndex)
        {
            ArgumentNullException.ThrowIfNull(s);
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            if (s.Length - charIndex < charCount)
                throw new ArgumentOutOfRangeException(nameof(s), SR.ArgumentOutOfRange_IndexCount);

            if (byteIndex < 0 || byteIndex > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(byteIndex), SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);

            return GetBytesCore(s.AsSpan(charIndex, charCount), new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex), counting: false, null);
        }

        // Encodes a range of characters in a character array into a range of bytes
        // in a byte array. An exception occurs if the byte array is not large
        // enough to hold the complete encoding of the characters. The
        // GetByteCount method can be used to determine the exact number of
        // bytes that will be produced for a given range of characters.
        // Alternatively, the GetMaxByteCount method can be used to
        // determine the maximum number of bytes that will be produced for a given
        // number of characters, regardless of the actual character values.
        //
        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding
        // parent method is safe

        public override int GetBytes(char[] chars, int charIndex, int charCount,
                                     byte[] bytes, int byteIndex)
        {
            // Validate parameters
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            if (chars.Length - charIndex < charCount)
                throw new ArgumentOutOfRangeException(nameof(chars), SR.ArgumentOutOfRange_IndexCountBuffer);

            if (byteIndex < 0 || byteIndex > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(byteIndex), SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);

            // If nothing to encode return 0
            if (charCount == 0)
                return 0;

            return GetBytesCore(new ReadOnlySpan<char>(chars, charIndex, charCount), new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex), counting: false, null);
        }

        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding

        [CLSCompliant(false)]
        public override unsafe int GetBytes(char* chars, int charCount, byte* bytes, int byteCount)
        {
            // Validate Parameters
            ArgumentNullException.ThrowIfNull(chars);
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            return GetBytesCore(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), counting: false, null);
        }

        public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes) =>
            GetBytesCore(chars, bytes, counting: false, null);

        // Returns the number of characters produced by decoding a range of bytes
        // in a byte array.
        //
        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding
        // parent method is safe

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            // Validate Parameters
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (bytes.Length - index < count)
                throw new ArgumentOutOfRangeException(nameof(bytes), SR.ArgumentOutOfRange_IndexCountBuffer);

            // If no input just return 0, fixed doesn't like 0 length arrays.
            if (count == 0)
                return 0;

            return GetCharCountCore(new ReadOnlySpan<byte>(bytes, index, count), null);
        }

        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding

        [CLSCompliant(false)]
        public override unsafe int GetCharCount(byte* bytes, int count)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(count);

            return GetCharCountCore(new ReadOnlySpan<byte>(bytes, count), null);
        }

        public override int GetCharCount(ReadOnlySpan<byte> bytes) => GetCharCountCore(bytes, null);

        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding
        // parent method is safe

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount,
                                     char[] chars, int charIndex)
        {
            // Validate Parameters
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            if (bytes.Length - byteIndex < byteCount)
                throw new ArgumentOutOfRangeException(nameof(bytes), SR.ArgumentOutOfRange_IndexCountBuffer);

            if (charIndex < 0 || charIndex > chars.Length)
                throw new ArgumentOutOfRangeException(nameof(charIndex), SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);

            // If no input, return 0 & avoid fixed problem
            if (byteCount == 0)
                return 0;

            return GetCharsCore(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), new Span<char>(chars, charIndex, chars.Length - charIndex), counting: false, null);
        }

        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding

        [CLSCompliant(false)]
        public override unsafe int GetChars(byte* bytes, int byteCount, char* chars, int charCount)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(chars);

            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            return GetCharsCore(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), counting: false, null);
        }

        public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars) =>
            GetCharsCore(bytes, chars, counting: false, null);

        // Returns a string containing the decoded representation of a range of
        // bytes in a byte array.
        //
        // All of our public Encodings that don't use EncodingNLS must have this (including EncodingNLS)
        // So if you fix this, fix the others.  Currently those include:
        // EncodingNLS, UTF7Encoding, UTF8Encoding, UTF32Encoding, ASCIIEncoding, UnicodeEncoding
        // parent method is safe

        public override string GetString(byte[] bytes, int index, int count)
        {
            // Validate Parameters
            ArgumentNullException.ThrowIfNull(bytes);

            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (bytes.Length - index < count)
                throw new ArgumentOutOfRangeException(nameof(bytes), SR.ArgumentOutOfRange_IndexCountBuffer);

            // Avoid problems with empty input buffer
            if (count == 0) return string.Empty;

            return GetString(new ReadOnlySpan<byte>(bytes, index, count));
        }

        //
        // End of standard methods copied from EncodingNLS.cs
        //
        private int GetByteCountCore(ReadOnlySpan<char> chars, Encoder? baseEncoder)
        {
            // Just call GetBytes with bytes is null
            return GetBytesCore(chars, default, counting: true, baseEncoder);
        }

        private int GetBytesCore(
            ReadOnlySpan<char> chars, Span<byte> bytes, bool counting, Encoder? baseEncoder)
        {
            // Get encoder info
            Encoder? encoder = baseEncoder;

            // Default bits & count
            int bits = 0;
            int bitCount = -1;

            // prepare our helpers
            EncodingByteBuffer buffer = new EncodingByteBuffer(
                this, encoder, bytes, counting, chars);

            if (encoder is not null)
            {
                bits = encoder.bits;
                bitCount = encoder.bitCount;

                // May have had too many left over
                while (bitCount >= 6)
                {
                    bitCount -= 6;
                    // If we fail we'll never really have enough room
                    if (!buffer.AddByte(_base64Bytes[(bits >> bitCount) & 0x3F]))
                        ThrowBytesOverflow(encoder, buffer.Count == 0);
                }
            }

            while (buffer.MoreData)
            {
                char currentChar = buffer.GetNextChar();

                if (currentChar < 0x80 && _directEncode[currentChar])
                {
                    if (bitCount >= 0)
                    {
                        if (bitCount > 0)
                        {
                            // Try to add the next byte
                            if (!buffer.AddByte(_base64Bytes[bits << 6 - bitCount & 0x3F]))
                                break;                                          // Stop here, didn't throw

                            bitCount = 0;
                        }

                        // Need to get emit '-' and our char, 2 bytes total
                        if (!buffer.AddByte((byte)'-'))
                            break;                                          // Stop here, didn't throw

                        bitCount = -1;
                    }

                    // Need to emit our char
                    if (!buffer.AddByte((byte)currentChar))
                        break;                                          // Stop here, didn't throw
                }
                else if (bitCount < 0 && currentChar == '+')
                {
                    if (!buffer.AddByte((byte)'+', (byte)'-'))
                        break;                                          // Stop here, didn't throw
                }
                else
                {
                    if (bitCount < 0)
                    {
                        // Need to emit a + and 12 bits (3 bytes)
                        // Only 12 of the 16 bits will be emitted this time, the other 4 wait 'til next time
                        if (!buffer.AddByte((byte)'+'))
                            break;                                          // Stop here, didn't throw

                        // We're now in bit mode, but haven't stored data yet
                        bitCount = 0;
                    }

                    // Add our bits
                    bits = bits << 16 | currentChar;
                    bitCount += 16;

                    while (bitCount >= 6)
                    {
                        bitCount -= 6;
                        if (!buffer.AddByte(_base64Bytes[(bits >> bitCount) & 0x3F]))
                        {
                            bitCount += 6;                              // We didn't use these bits
                            buffer.GetNextChar();                       // We're processing this char still, but AddByte
                                                                        // --'d it when we ran out of space
                            break;                                      // Stop here, not enough room for bytes
                        }
                    }

                    if (bitCount >= 6)
                        break;                  // Didn't have room to encode enough bits
                }
            }

            // Now if we have bits left over we have to encode them.
            // MustFlush may have been cleared by encoding.ThrowBytesOverflow earlier if converting
            if (bitCount >= 0 && (encoder is null || encoder.MustFlush))
            {
                // Do we have bits we have to stick in?
                if (bitCount > 0)
                {
                    if (buffer.AddByte(_base64Bytes[(bits << (6 - bitCount)) & 0x3F]))
                    {
                        // Emitted spare bits, 0 bits left
                        bitCount = 0;
                    }
                }

                // If converting and failed bitCount above, then we'll fail this too
                if (buffer.AddByte((byte)'-'))
                {
                    // turned off bit mode';
                    bits = 0;
                    bitCount = -1;
                }
                else
                    // If not successful, convert will maintain state for next time, also
                    // AddByte will have decremented our char count, however we need it to remain the same
                    buffer.GetNextChar();
            }

            // Do we have an encoder we're allowed to use?
            // bytes is null if counting, so don't use encoder then
            if (!counting && encoder is not null)
            {
                // We already cleared bits & bitcount for mustflush case
                encoder.bits = bits;
                encoder.bitCount = bitCount;
                encoder._charsUsed = buffer.CharsUsed;
            }

            return buffer.Count;
        }

        private int GetCharCountCore(ReadOnlySpan<byte> bytes, Decoder? baseDecoder)
        {
            // Just call GetChars with null char* to do counting
            return GetCharsCore(bytes, default, counting: true, baseDecoder);
        }

        private int GetCharsCore(
            ReadOnlySpan<byte> bytes, Span<char> chars, bool counting, Decoder? baseDecoder)
        {
            // Might use a decoder
            Decoder? decoder = baseDecoder;

            // Get our output buffer info.
            EncodingCharBuffer buffer = new EncodingCharBuffer(
                this, decoder, chars, counting, bytes);

            // Get decoder info
            int bits = 0;
            int bitCount = -1;
            bool firstByte = false;
            if (decoder is not null)
            {
                bits = decoder.bits;
                bitCount = decoder.bitCount;
                firstByte = decoder.firstByte;

                Debug.Assert(!firstByte || decoder.bitCount <= 0,
                    "[UTF7Encoding.GetChars]If remembered bits, then first byte flag shouldn't be set");
            }

            // We may have had bits in the decoder that we couldn't output last time, so do so now
            if (bitCount >= 16)
            {
                // Check our decoder buffer
                if (!buffer.AddChar((char)((bits >> (bitCount - 16)) & 0xFFFF)))
                    ThrowCharsOverflow(decoder, true);  // Always throw, they need at least 1 char even in Convert

                // Used this one, clean up extra bits
                bitCount -= 16;
            }

            // Loop through the input
            while (buffer.MoreData)
            {
                byte currentByte = buffer.GetNextByte();
                int c;

                if (bitCount >= 0)
                {
                    //
                    // Modified base 64 encoding.
                    //
                    sbyte v;
                    if (currentByte < 0x80 && ((v = _base64Values[currentByte]) >= 0))
                    {
                        firstByte = false;
                        bits = (bits << 6) | ((byte)v);
                        bitCount += 6;
                        if (bitCount >= 16)
                        {
                            c = (bits >> (bitCount - 16)) & 0xFFFF;
                            bitCount -= 16;
                        }
                        // If not enough bits just continue
                        else continue;
                    }
                    else
                    {
                        // If it wasn't a base 64 byte, everything's going to turn off base 64 mode
                        bitCount = -1;

                        if (currentByte != '-')
                        {
                            // >= 0x80 (because of 1st if statemtn)
                            // We need this check since the _base64Values[b] check below need b <= 0x7f.
                            // This is not a valid base 64 byte.  Terminate the shifted-sequence and
                            // emit this byte.

                            // not in base 64 table
                            // According to the RFC 1642 and the example code of UTF-7
                            // in Unicode 2.0, we should just zero-extend the invalid UTF7 byte

                            // Chars won't be updated unless this works, try to fallback
                            if (!buffer.Fallback(currentByte))
                                break;                                          // Stop here, didn't throw

                            // Used that byte, we're done with it
                            continue;
                        }

                        //
                        // The encoding for '+' is "+-".
                        //
                        if (firstByte) c = '+';
                        // We just turn it off if not emitting a +, so we're done.
                        else continue;
                    }
                    //
                    // End of modified base 64 encoding block.
                    //
                }
                else if (currentByte == '+')
                {
                    //
                    // Found the start of a modified base 64 encoding block or a plus sign.
                    //
                    bitCount = 0;
                    firstByte = true;
                    continue;
                }
                else
                {
                    // Normal character
                    if (currentByte >= 0x80)
                    {
                        // Try to fallback
                        if (!buffer.Fallback(currentByte))
                            break;                                          // Stop here, didn't throw

                        // Done falling back
                        continue;
                    }

                    // Use the normal character
                    c = currentByte;
                }

                if (c >= 0)
                {
                    // Check our buffer
                    if (!buffer.AddChar((char)c))
                    {
                        // No room.  If it was a plain char we'll try again later.
                        // Note, we'll consume this byte and stick it in decoder, even if we can't output it
                        if (bitCount >= 0)                                  // Can we rememmber this byte (char)
                        {
                            buffer.AdjustBytes(+1);                         // Need to readd the byte that AddChar subtracted when it failed
                            bitCount += 16;                                 // We'll still need that char we have in our bits
                        }
                        break;                                              // didn't throw, stop
                    }
                }
            }

            // Stick stuff in the decoder if we can (chars is null if counting, so don't store decoder)
            if (!counting && decoder is not null)
            {
                // MustFlush?  (Could've been cleared by ThrowCharsOverflow if Convert & didn't reach end of buffer)
                if (decoder.MustFlush)
                {
                    // RFC doesn't specify what would happen if we have non-0 leftover bits, we just drop them
                    decoder.bits = 0;
                    decoder.bitCount = -1;
                    decoder.firstByte = false;
                }
                else
                {
                    decoder.bits = bits;
                    decoder.bitCount = bitCount;
                    decoder.firstByte = firstByte;
                }
                decoder._bytesUsed = buffer.BytesUsed;
            }
            // else ignore any hanging bits.

            // Return our count
            return buffer.Count;
        }

        public override Text.Decoder GetDecoder()
        {
            return new Decoder(this);
        }

        public override Text.Encoder GetEncoder()
        {
            return new Encoder(this);
        }

        public override int GetMaxByteCount(int charCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);

            // Suppose that every char can not be direct-encoded, we know that
            // a byte can encode 6 bits of the Unicode character.  And we will
            // also need two extra bytes for the shift-in ('+') and shift-out ('-') mark.
            // Therefore, the max byte should be:
            // byteCount = 2 + Math.Ceiling((double)charCount * 16 / 6);
            // That is always <= 2 + 3 * charCount;
            // Longest case is alternating encoded, direct, encoded data for 5 + 1 + 5... bytes per char.
            // UTF7 doesn't have left over surrogates, but if no input we may need an output - to turn off
            // encoding if MustFlush is true.

            // Its easiest to think of this as 2 bytes to turn on/off the base64 mode, then 3 bytes per char.
            // 3 bytes is 18 bits of encoding, which is more than we need, but if its direct encoded then 3
            // bytes allows us to turn off and then back on base64 mode if necessary.

            // Note that UTF7 encoded surrogates individually and isn't worried about mismatches, so all
            // code points are encodable int UTF7.
            long byteCount = (long)charCount * 3 + 2;

            // check for overflow
            if (byteCount > 0x7fffffff)
                throw new ArgumentOutOfRangeException(nameof(charCount), SR.ArgumentOutOfRange_GetByteCountOverflow);

            return (int)byteCount;
        }

        public override int GetMaxCharCount(int byteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

            // Worst case is 1 char per byte.  Minimum 1 for left over bits in case decoder is being flushed
            // Also note that we ignore extra bits (per spec), so UTF7 doesn't have unknown in this direction.
            int charCount = byteCount;
            if (charCount == 0) charCount = 1;

            return charCount;
        }

        // NetWasm: Encoding.ThrowBytesOverflow(EncoderNLS, bool) for this codec.
        private void ThrowBytesOverflow(Encoder? encoder, bool nothingEncoded)
        {
            if (encoder is null || encoder._throwOnOverflow || nothingEncoded)
            {
                encoder?.FallbackBuffer.Reset();

                // Special message to include fallback type in case fallback's GetMaxCharCount is broken
                // This happens if user has implemented an encoder fallback with a broken GetMaxCharCount
                ThrowBytesOverflow();
            }

            // If we didn't throw, we are in convert and have to remember our flushing
            encoder.ClearMustFlush();
        }

        [DoesNotReturn]
        private void ThrowBytesOverflow() =>
            // Special message to include fallback type in case fallback's GetMaxCharCount is broken
            // This happens if user has implemented an encoder fallback with a broken GetMaxCharCount
            throw new ArgumentException(SR.Format(SR.Argument_EncodingConversionOverflowBytes, EncodingName, EncoderFallback.GetType()), "bytes");

        // NetWasm: Encoding.ThrowCharsOverflow(DecoderNLS, bool) for this codec.
        private void ThrowCharsOverflow(Decoder? decoder, bool nothingDecoded)
        {
            if (decoder is null || decoder._throwOnOverflow || nothingDecoded)
            {
                decoder?.FallbackBuffer.Reset();

                // Special message to include fallback type in case fallback's GetMaxCharCount is broken
                // This happens if user has implemented a decoder fallback with a broken GetMaxCharCount
                ThrowCharsOverflow();
            }

            // If we didn't throw, we are in convert and have to remember our flushing
            decoder.ClearMustFlush();
        }

        [DoesNotReturn]
        private void ThrowCharsOverflow() =>
            // Special message to include fallback type in case fallback's GetMaxCharCount is broken
            // This happens if user has implemented a decoder fallback with a broken GetMaxCharCount
            throw new ArgumentException(SR.Format(SR.Argument_EncodingConversionOverflowChars, EncodingName, DecoderFallback.GetType()), "chars");

        // Of all the amazing things... This MUST be Decoder so that our com name
        // for System.Text.Decoder doesn't change
        // NetWasm: derives from System.Text.Decoder and carries DecoderNLS's validation, MustFlush
        // and throw-on-overflow handling.
        private sealed class Decoder : Text.Decoder
        {
            private readonly UTF7Encoding _utf7Encoding;
            private bool _mustFlush;
            internal bool _throwOnOverflow;
            internal int _bytesUsed;

            /*private*/
            internal int bits;
            /*private*/
            internal int bitCount;
            /*private*/
            internal bool firstByte;

            public Decoder(UTF7Encoding encoding)
            {
                _utf7Encoding = encoding;
                Initialize(encoding);
                Fallback = encoding.DecoderFallback;
                this.Reset();
            }

            public override void Reset()
            {
                this.bits = 0;
                this.bitCount = -1;
                this.firstByte = false;
                base.Reset();
            }

            public override int GetCharCount(byte[] bytes, int index, int count)
            {
                return GetCharCount(bytes, index, count, false);
            }

            public override int GetCharCount(byte[] bytes, int index, int count, bool flush)
            {
                ArgumentNullException.ThrowIfNull(bytes);

                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfNegative(count);

                if (bytes.Length - index < count)
                    throw new ArgumentOutOfRangeException(nameof(bytes),
                        SR.ArgumentOutOfRange_IndexCountBuffer);

                return CharCount(new ReadOnlySpan<byte>(bytes, index, count), flush);
            }

            public override unsafe int GetCharCount(byte* bytes, int count, bool flush)
            {
                ArgumentNullException.ThrowIfNull(bytes);

                ArgumentOutOfRangeException.ThrowIfNegative(count);

                return CharCount(new ReadOnlySpan<byte>(bytes, count), flush);
            }

            public override int GetCharCount(ReadOnlySpan<byte> bytes, bool flush) => CharCount(bytes, flush);

            private int CharCount(ReadOnlySpan<byte> bytes, bool flush)
            {
                // Remember the flush
                _mustFlush = flush;
                _throwOnOverflow = true;

                return _utf7Encoding.GetCharCountCore(bytes, this);
            }

            public override int GetChars(byte[] bytes, int byteIndex, int byteCount,
                                         char[] chars, int charIndex)
            {
                return GetChars(bytes, byteIndex, byteCount, chars, charIndex, false);
            }

            public override int GetChars(byte[] bytes, int byteIndex, int byteCount,
                                         char[] chars, int charIndex, bool flush)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                ArgumentNullException.ThrowIfNull(chars);

                ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

                if (bytes.Length - byteIndex < byteCount)
                    throw new ArgumentOutOfRangeException(nameof(bytes),
                        SR.ArgumentOutOfRange_IndexCountBuffer);

                if (charIndex < 0 || charIndex > chars.Length)
                    throw new ArgumentOutOfRangeException(nameof(charIndex),
                        SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);

                return Chars(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount),
                             new Span<char>(chars, charIndex, chars.Length - charIndex), flush);
            }

            public override unsafe int GetChars(byte* bytes, int byteCount,
                                                char* chars, int charCount, bool flush)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                ArgumentNullException.ThrowIfNull(chars);

                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
                ArgumentOutOfRangeException.ThrowIfNegative(charCount);

                return Chars(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), flush);
            }

            public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush) => Chars(bytes, chars, flush);

            private int Chars(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush)
            {
                // Remember our flush
                _mustFlush = flush;
                _throwOnOverflow = true;

                return _utf7Encoding.GetCharsCore(bytes, chars, counting: false, this);
            }

            public override void Convert(byte[] bytes, int byteIndex, int byteCount,
                                         char[] chars, int charIndex, int charCount, bool flush,
                                         out int bytesUsed, out int charsUsed, out bool completed)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                ArgumentNullException.ThrowIfNull(chars);

                ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

                ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(charCount);

                if (bytes.Length - byteIndex < byteCount)
                    throw new ArgumentOutOfRangeException(nameof(bytes),
                          SR.ArgumentOutOfRange_IndexCountBuffer);

                if (chars.Length - charIndex < charCount)
                    throw new ArgumentOutOfRangeException(nameof(chars),
                          SR.ArgumentOutOfRange_IndexCountBuffer);

                ConvertCore(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), new Span<char>(chars, charIndex, charCount), flush,
                    out bytesUsed, out charsUsed, out completed);
            }

            public override unsafe void Convert(byte* bytes, int byteCount,
                                                char* chars, int charCount, bool flush,
                                                out int bytesUsed, out int charsUsed, out bool completed)
            {
                ArgumentNullException.ThrowIfNull(bytes);
                ArgumentNullException.ThrowIfNull(chars);

                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
                ArgumentOutOfRangeException.ThrowIfNegative(charCount);

                ConvertCore(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), flush,
                    out bytesUsed, out charsUsed, out completed);
            }

            public override void Convert(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush,
                                         out int bytesUsed, out int charsUsed, out bool completed) =>
                ConvertCore(bytes, chars, flush, out bytesUsed, out charsUsed, out completed);

            private void ConvertCore(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush,
                                     out int bytesUsed, out int charsUsed, out bool completed)
            {
                // We don't want to throw
                _mustFlush = flush;
                _throwOnOverflow = false;
                _bytesUsed = 0;

                // Do conversion
                charsUsed = _utf7Encoding.GetCharsCore(bytes, chars, counting: false, this);
                bytesUsed = _bytesUsed;

                // It's completed if they've used what they wanted AND if they didn't want flush or if we are flushed
                // Its also completed if we've used all the data and there's no fallback data left
                completed = (bytesUsed == bytes.Length)
                    && (!flush || !this.HasState)
                    && FallbackBuffer.Remaining == 0;
            }

            internal bool MustFlush => _mustFlush;

            // Anything left in our encoder?
            internal bool HasState =>
                // NOTE: This forces the last -, which some encoder might not encode.  If we
                // don't see it we don't think we're done reading.
                this.bitCount != -1;

            internal void ClearMustFlush()
            {
                _mustFlush = false;
            }
        }

        // Of all the amazing things... This MUST be Encoder so that our com name
        // for System.Text.Encoder doesn't change
        // NetWasm: derives from System.Text.Encoder and carries EncoderNLS's validation, MustFlush
        // and throw-on-overflow handling.
        private sealed class Encoder : Text.Encoder
        {
            private readonly UTF7Encoding _utf7Encoding;
            private bool _mustFlush;
            internal bool _throwOnOverflow;
            internal int _charsUsed;

            /*private*/
            internal int bits;
            /*private*/
            internal int bitCount;

            public Encoder(UTF7Encoding encoding)
            {
                _utf7Encoding = encoding;
                Initialize(encoding);
                Fallback = encoding.EncoderFallback;
                this.Reset();
            }

            public override void Reset()
            {
                this.bitCount = -1;
                this.bits = 0;
                base.Reset();
            }

            public override int GetByteCount(char[] chars, int index, int count, bool flush)
            {
                ArgumentNullException.ThrowIfNull(chars);

                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfNegative(count);

                if (chars.Length - index < count)
                    throw new ArgumentOutOfRangeException(nameof(chars),
                          SR.ArgumentOutOfRange_IndexCountBuffer);

                return ByteCount(new ReadOnlySpan<char>(chars, index, count), flush);
            }

            public override unsafe int GetByteCount(char* chars, int count, bool flush)
            {
                ArgumentNullException.ThrowIfNull(chars);

                ArgumentOutOfRangeException.ThrowIfNegative(count);

                return ByteCount(new ReadOnlySpan<char>(chars, count), flush);
            }

            public override int GetByteCount(ReadOnlySpan<char> chars, bool flush) => ByteCount(chars, flush);

            private int ByteCount(ReadOnlySpan<char> chars, bool flush)
            {
                _mustFlush = flush;
                _throwOnOverflow = true;

                return _utf7Encoding.GetByteCountCore(chars, this);
            }

            public override int GetBytes(char[] chars, int charIndex, int charCount,
                                         byte[] bytes, int byteIndex, bool flush)
            {
                ArgumentNullException.ThrowIfNull(chars);
                ArgumentNullException.ThrowIfNull(bytes);

                ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(charCount);

                if (chars.Length - charIndex < charCount)
                    throw new ArgumentOutOfRangeException(nameof(chars),
                          SR.ArgumentOutOfRange_IndexCountBuffer);

                if (byteIndex < 0 || byteIndex > bytes.Length)
                    throw new ArgumentOutOfRangeException(nameof(byteIndex),
                         SR.ArgumentOutOfRange_IndexMustBeLessOrEqual);

                return Bytes(new ReadOnlySpan<char>(chars, charIndex, charCount),
                             new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex), flush);
            }

            public override unsafe int GetBytes(char* chars, int charCount, byte* bytes, int byteCount, bool flush)
            {
                ArgumentNullException.ThrowIfNull(chars);
                ArgumentNullException.ThrowIfNull(bytes);

                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
                ArgumentOutOfRangeException.ThrowIfNegative(charCount);

                return Bytes(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), flush);
            }

            public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush) => Bytes(chars, bytes, flush);

            private int Bytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush)
            {
                _mustFlush = flush;
                _throwOnOverflow = true;

                return _utf7Encoding.GetBytesCore(chars, bytes, counting: false, this);
            }

            // This method is used when your output buffer might not be large enough for the entire result.
            // Just call the pointer version.  (This gets bytes)
            public override void Convert(char[] chars, int charIndex, int charCount,
                                         byte[] bytes, int byteIndex, int byteCount, bool flush,
                                         out int charsUsed, out int bytesUsed, out bool completed)
            {
                ArgumentNullException.ThrowIfNull(chars);
                ArgumentNullException.ThrowIfNull(bytes);

                ArgumentOutOfRangeException.ThrowIfNegative(charIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(charCount);
                ArgumentOutOfRangeException.ThrowIfNegative(byteIndex);
                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

                if (chars.Length - charIndex < charCount)
                    throw new ArgumentOutOfRangeException(nameof(chars),
                          SR.ArgumentOutOfRange_IndexCountBuffer);

                if (bytes.Length - byteIndex < byteCount)
                    throw new ArgumentOutOfRangeException(nameof(bytes),
                          SR.ArgumentOutOfRange_IndexCountBuffer);

                ConvertCore(new ReadOnlySpan<char>(chars, charIndex, charCount), new Span<byte>(bytes, byteIndex, byteCount), flush,
                    out charsUsed, out bytesUsed, out completed);
            }

            // This is the version that uses pointers.  We call the base encoding worker function
            // after setting our appropriate internal variables.  This is getting bytes
            public override unsafe void Convert(char* chars, int charCount,
                                                byte* bytes, int byteCount, bool flush,
                                                out int charsUsed, out int bytesUsed, out bool completed)
            {
                ArgumentNullException.ThrowIfNull(chars);
                ArgumentNullException.ThrowIfNull(bytes);

                ArgumentOutOfRangeException.ThrowIfNegative(charCount);
                ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

                ConvertCore(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), flush,
                    out charsUsed, out bytesUsed, out completed);
            }

            public override void Convert(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush,
                                         out int charsUsed, out int bytesUsed, out bool completed) =>
                ConvertCore(chars, bytes, flush, out charsUsed, out bytesUsed, out completed);

            private void ConvertCore(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush,
                                     out int charsUsed, out int bytesUsed, out bool completed)
            {
                // We don't want to throw
                _mustFlush = flush;
                _throwOnOverflow = false;
                _charsUsed = 0;

                // Do conversion
                bytesUsed = _utf7Encoding.GetBytesCore(chars, bytes, counting: false, this);
                charsUsed = _charsUsed;

                // Its completed if they've used what they wanted AND if they didn't want flush or if we are flushed
                completed = (charsUsed == chars.Length)
                    && (!flush || !this.HasState)
                    && FallbackBuffer.Remaining == 0;

                // Our data thingy are now full, we can return
            }

            internal bool MustFlush => _mustFlush;

            // Anything left in our encoder?
            internal bool HasState => this.bits != 0 || this.bitCount != -1;

            internal void ClearMustFlush()
            {
                _mustFlush = false;
            }
        }

        // Preexisting UTF7 behavior for bad bytes was just to spit out the byte as the next char
        // and turn off base64 mode if it was in that mode.  We still exit the mode, but now we fallback.
        private sealed class DecoderUTF7Fallback : DecoderFallback
        {
            // Default replacement fallback uses no best fit and ? replacement string

            public override DecoderFallbackBuffer CreateFallbackBuffer() =>
                new DecoderUTF7FallbackBuffer();

            // Maximum number of characters that this instance of this fallback could return
            public override int MaxCharCount => 1; // returns 1 char per bad byte

            public override bool Equals([NotNullWhen(true)] object? value) => value is DecoderUTF7Fallback;

            public override int GetHashCode() => 984;
        }

        private sealed class DecoderUTF7FallbackBuffer : DecoderFallbackBuffer
        {
            // Store our default string
            private char cFallback;
            private int iCount = -1;
            private int iSize;

            // Fallback Methods
            public override bool Fallback(byte[] bytesUnknown, int index)
            {
                // We expect no previous fallback in our buffer
                Debug.Assert(iCount < 0, "[DecoderUTF7FallbackBuffer.Fallback] Can't have recursive fallbacks");
                Debug.Assert(bytesUnknown.Length == 1, "[DecoderUTF7FallbackBuffer.Fallback] Only possible fallback case should be 1 unknown byte");

                // Go ahead and get our fallback
                cFallback = (char)bytesUnknown[0];

                // Any of the fallback characters can be handled except for 0
                if (cFallback == 0)
                {
                    return false;
                }

                iCount = iSize = 1;

                return true;
            }

            public override char GetNextChar()
            {
                if (iCount-- > 0)
                    return cFallback;

                // Note: this means that 0 in UTF7 stream will never be emitted.
                return (char)0;
            }

            public override bool MovePrevious()
            {
                if (iCount >= 0)
                {
                    iCount++;
                }

                // return true if we were allowed to do this
                return iCount >= 0 && iCount <= iSize;
            }

            // Return # of chars left in this fallback
            public override int Remaining => (iCount > 0) ? iCount : 0;

            // Clear the buffer
            public override void Reset()
            {
                iCount = -1;
            }
        }

        // NetWasm: the parts of upstream's internal EncodingByteBuffer that UTF-7 uses, over spans.
        // UTF-7 never falls back while encoding, so the encoder's fallback buffer is not consulted.
        private ref struct EncodingByteBuffer
        {
            private readonly UTF7Encoding _enc;
            private readonly Encoder? _encoder;
            private readonly Span<byte> _bytes;
            private readonly bool _counting;
            private readonly ReadOnlySpan<char> _chars;
            private int _byteIndex;
            private int _charIndex;
            private int _byteCountResult;

            internal EncodingByteBuffer(UTF7Encoding inEncoding, Encoder? inEncoder,
                        Span<byte> inByteStart, bool counting, ReadOnlySpan<char> inCharStart)
            {
                _enc = inEncoding;
                _encoder = inEncoder;
                _bytes = inByteStart;
                _counting = counting;
                _chars = inCharStart;
                _byteIndex = 0;
                _charIndex = 0;
                _byteCountResult = 0;
            }

            internal bool AddByte(byte b, int moreBytesExpected)
            {
                Debug.Assert(moreBytesExpected >= 0, "[EncodingByteBuffer.AddByte]expected non-negative moreBytesExpected");
                if (!_counting)
                {
                    if (_byteIndex >= _bytes.Length - moreBytesExpected)
                    {
                        // Throw maybe.  Check which buffer to back up (only matters if Converting)
                        this.MovePrevious(true);            // Throw if necessary
                        return false;                       // No throw, but no store either
                    }

                    _bytes[_byteIndex++] = b;
                }
                _byteCountResult++;
                return true;
            }

            internal bool AddByte(byte b1)
            {
                return AddByte(b1, 0);
            }

            internal bool AddByte(byte b1, byte b2)
            {
                return AddByte(b1, b2, 0);
            }

            internal bool AddByte(byte b1, byte b2, int moreBytesExpected)
            {
                return AddByte(b1, 1 + moreBytesExpected) && AddByte(b2, moreBytesExpected);
            }

            internal void MovePrevious(bool bThrow)
            {
                if (_charIndex > 0)
                    _charIndex--;

                if (bThrow)
                    _enc.ThrowBytesOverflow(_encoder, _byteIndex == 0);
            }

            internal bool MoreData => _charIndex < _chars.Length;

            internal char GetNextChar()
            {
                char cReturn = (char)0;
                if (_charIndex < _chars.Length)
                    cReturn = _chars[_charIndex++];

                return cReturn;
            }

            internal int CharsUsed => _charIndex;

            internal int Count => _byteCountResult;
        }

        // NetWasm: the parts of upstream's internal EncodingCharBuffer that UTF-7 uses, over spans,
        // with DecoderFallbackBuffer.InternalFallback expressed through the public fallback contract.
        private ref struct EncodingCharBuffer
        {
            private readonly UTF7Encoding _enc;
            private readonly Decoder? _decoder;
            private readonly Span<char> _chars;
            private readonly bool _counting;
            private readonly ReadOnlySpan<byte> _bytes;
            private int _charIndex;
            private int _byteIndex;
            private int _charCountResult;
            private readonly DecoderFallbackBuffer _fallbackBuffer;

            internal EncodingCharBuffer(UTF7Encoding enc, Decoder? decoder, Span<char> charStart, bool counting,
                                        ReadOnlySpan<byte> byteStart)
            {
                _enc = enc;
                _decoder = decoder;

                _chars = charStart;
                _counting = counting;
                _bytes = byteStart;
                _charIndex = 0;
                _byteIndex = 0;
                _charCountResult = 0;

                if (decoder is null)
                    _fallbackBuffer = enc.DecoderFallback.CreateFallbackBuffer();
                else
                    _fallbackBuffer = decoder.FallbackBuffer;

                // If we're getting chars or getting char count we don't expect to have
                // to remember fallbacks between calls (so it should be empty)
                Debug.Assert(_fallbackBuffer.Remaining == 0,
                    "[Encoding.EncodingCharBuffer.EncodingCharBuffer]Expected empty fallback buffer for getchars/charcount");
            }

            internal bool AddChar(char ch, int numBytes)
            {
                if (!_counting)
                {
                    if (_charIndex >= _chars.Length)
                    {
                        // Throw maybe
                        _byteIndex -= numBytes;                                        // Didn't encode these bytes
                        _enc.ThrowCharsOverflow(_decoder, _byteIndex <= 0);    // Throw?
                        return false;                                           // No throw, but no store either
                    }

                    _chars[_charIndex++] = ch;
                }
                _charCountResult++;
                return true;
            }

            internal bool AddChar(char ch)
            {
                return AddChar(ch, 1);
            }

            internal void AdjustBytes(int count)
            {
                _byteIndex += count;
            }

            internal bool MoreData => _byteIndex < _bytes.Length;

            internal byte GetNextByte()
            {
                if (_byteIndex >= _bytes.Length)
                    return 0;
                return _bytes[_byteIndex++];
            }

            internal int BytesUsed => _byteIndex;

            internal bool Fallback(byte fallbackByte)
            {
                // Build our buffer
                byte[] byteBuffer = new byte[] { fallbackByte };

                // Do the fallback and add the data.
                return Fallback(byteBuffer);
            }

            internal bool Fallback(byte[] byteBuffer)
            {
                // Do the fallback and add the data.
                if (!_counting)
                {
                    int charStart = _charIndex;
                    if (!InternalFallback(byteBuffer, ref _charIndex))
                    {
                        // Throw maybe
                        _byteIndex -= byteBuffer.Length;                             // Didn't use how many ever bytes we're falling back
                        _fallbackBuffer.Reset();                                     // We didn't use this fallback.
                        _enc.ThrowCharsOverflow(_decoder, _charIndex == 0);          // Throw?
                        return false;                                           // No throw, but no store either
                    }
                    _charCountResult += _charIndex - charStart;
                }
                else
                {
                    _charCountResult += InternalFallback(byteBuffer);
                }

                return true;
            }

            // DecoderFallbackBuffer.InternalFallback(byte[], byte*, ref char*): copy the fallback
            // characters to the output, or report that they do not fit.
            private bool InternalFallback(byte[] bytes, ref int chars)
            {
                // See if there's a fallback character and we have an output buffer then copy our string.
                if (_fallbackBuffer.Fallback(bytes, _byteIndex - bytes.Length))
                {
                    // Copy the chars to our output
                    char ch;
                    int charTemp = chars;
                    bool bHighSurrogate = false;
                    while ((ch = _fallbackBuffer.GetNextChar()) != 0)
                    {
                        // Make sure no mixed up surrogates
                        if (char.IsSurrogate(ch))
                        {
                            if (char.IsHighSurrogate(ch))
                            {
                                // High Surrogate
                                if (bHighSurrogate)
                                    throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex);
                                bHighSurrogate = true;
                            }
                            else
                            {
                                // Low surrogate
                                if (!bHighSurrogate)
                                    throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex);
                                bHighSurrogate = false;
                            }
                        }

                        if (charTemp >= _chars.Length)
                        {
                            // No buffer space
                            return false;
                        }

                        _chars[charTemp++] = ch;
                    }

                    // Need to make sure that bHighSurrogate isn't true
                    if (bHighSurrogate)
                        throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex);

                    // Now we aren't going to be false, so its OK to update chars
                    chars = charTemp;
                }

                return true;
            }

            // DecoderFallbackBuffer.InternalFallback(byte[], byte*): count the fallback characters.
            private int InternalFallback(byte[] bytes)
            {
                // See if there's a fallback character and we have an output buffer then copy our string.
                if (_fallbackBuffer.Fallback(bytes, _byteIndex - bytes.Length))
                {
                    int count = 0;

                    char ch;
                    bool bHighSurrogate = false;
                    while ((ch = _fallbackBuffer.GetNextChar()) != 0)
                    {
                        // Make sure no mixed up surrogates
                        if (char.IsSurrogate(ch))
                        {
                            if (char.IsHighSurrogate(ch))
                            {
                                // High Surrogate
                                if (bHighSurrogate)
                                    throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex);
                                bHighSurrogate = true;
                            }
                            else
                            {
                                // Low surrogate
                                if (!bHighSurrogate)
                                    throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex);
                                bHighSurrogate = false;
                            }
                        }

                        count++;
                    }

                    // Need to make sure that bHighSurrogate isn't true
                    if (bHighSurrogate)
                        throw new ArgumentException(SR.Argument_InvalidCharSequenceNoIndex);

                    return count;
                }

                // If no fallback return 0
                return 0;
            }

            internal int Count => _charCountResult;
        }
    }
}
