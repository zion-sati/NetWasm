// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce:
// src/libraries/System.Private.CoreLib/src/System/String.cs, less the members String.NetWasm.cs
// supplies: the fields and Empty, Length, the indexer and GetRawStringData.
// NetWasm adaptations: NetWasm's string declares no fields; the compiler reaches its characters and
// length through GetRawStringData() and Length, which replace upstream's _firstChar and _stringLength.
// The constructors are extern rather than extern safe, a modifier the SDK's C# does not have yet; as
// on CoreCLR, NativeAOT and Mono, the compiler turns each into a call to its Ctor method.

using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace System
{
    // The String class represents a static string of characters.  Many of
    // the string methods perform some type of transformation on the current
    // instance and return the result as a new string.  As with arrays, character
    // positions (indices) are zero-based.

    [Serializable]
    [NonVersionable] // This only applies to field layout
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed partial class String
        : IComparable,
          IEnumerable,
          IConvertible,
          IEnumerable<char>,
          IComparable<string?>,
          IEquatable<string?>,
          ICloneable,
          ISpanParsable<string>
    {
        /// <summary>Maximum length allowed for a string.</summary>
        /// <remarks>Keep in sync with AllocateString in gchelpers.cpp.</remarks>
        internal const int MaxLength = 0x3FFFFFDF;

        /*
         * CONSTRUCTORS
         *
         * Defining a new constructor for string-like types (like String) requires changes both
         * to the managed code below and to the native VM code. See the comment at the top of
         * src/vm/ecall.cpp for instructions on how to add new overloads.
         */

        /// <safety>The runtime FCall builds the string by copying from the supplied managed char array, whose length it reads safely; it takes no raw pointer and touches no caller-supplied unmanaged memory.</safety>
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.Char[])")]
#endif
        public extern String(char[]? value);

        private static string Ctor(char[]? value)
        {
            if (value == null || value.Length == 0)
                return Empty;

            string result = FastAllocateString(value.Length);

            Buffer.Memmove(
                elementCount: (uint)result.Length, // derefing Length now allows JIT to prove 'result' not null below
                destination: ref result.GetRawStringData(),
                source: ref MemoryMarshal.GetArrayDataReference(value));

            return result;
        }

        /// <safety>The runtime FCall validates the start index and length against the managed array's bounds before copying; it takes no raw pointer and touches no caller-supplied unmanaged memory.</safety>
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.Char[],System.Int32,System.Int32)")]
#endif
        public extern String(char[] value, int startIndex, int length);

        private static string Ctor(char[] value, int startIndex, int length)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, value.Length - length);

            if (length == 0)
                return Empty;

            string result = FastAllocateString(length);

            Buffer.Memmove(
                elementCount: (uint)result.Length, // derefing Length now allows JIT to prove 'result' not null below
                destination: ref result.GetRawStringData(),
                source: ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(value), startIndex));

            return result;
        }

        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.Char*)")]
#endif
        public extern unsafe String(char* value);

        private static unsafe string Ctor(char* ptr)
        {
            if (ptr == null)
                return Empty;

            int count = wcslen(ptr);
            if (count == 0)
                return Empty;

            string result = FastAllocateString(count);

            Buffer.Memmove(
                elementCount: (uint)result.Length, // derefing Length now allows JIT to prove 'result' not null below
                destination: ref result.GetRawStringData(),
                source: ref *ptr);

            return result;
        }

        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.Char*,System.Int32,System.Int32)")]
#endif
        public extern unsafe String(char* value, int startIndex, int length);

        private static unsafe string Ctor(char* ptr, int startIndex, int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);

            char* pStart = ptr + startIndex;

            // overflow check
            if (pStart < ptr)
                throw new ArgumentOutOfRangeException(nameof(startIndex), SR.ArgumentOutOfRange_PartialWCHAR);

            if (length == 0)
                return Empty;

            if (ptr == null)
                throw new ArgumentOutOfRangeException(nameof(ptr), SR.ArgumentOutOfRange_PartialWCHAR);

            string result = FastAllocateString(length);

            Buffer.Memmove(
               elementCount: (uint)result.Length, // derefing Length now allows JIT to prove 'result' not null below
               destination: ref result.GetRawStringData(),
               source: ref *pStart);

            return result;
        }

        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.SByte*)")]
#endif
        public extern unsafe String(sbyte* value);

        private static unsafe string Ctor(sbyte* value)
        {
            byte* pb = (byte*)value;
            if (pb == null)
                return Empty;

            int numBytes = strlen((byte*)value);

            return CreateStringForSByteConstructor(pb, numBytes);
        }

        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.SByte*,System.Int32,System.Int32)")]
#endif
        public extern unsafe String(sbyte* value, int startIndex, int length);

        private static unsafe string Ctor(sbyte* value, int startIndex, int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            if (value == null)
            {
                if (length == 0)
                    return Empty;

                ArgumentNullException.Throw(nameof(value));
            }

            byte* pStart = (byte*)(value + startIndex);

            // overflow check
            if (pStart < value)
                throw new ArgumentOutOfRangeException(nameof(value), SR.ArgumentOutOfRange_PartialWCHAR);

            return CreateStringForSByteConstructor(pStart, length);
        }

        // Encoder for String..ctor(sbyte*) and String..ctor(sbyte*, int, int)
        private static unsafe string CreateStringForSByteConstructor(byte* pb, int numBytes)
        {
            Debug.Assert(numBytes >= 0);
            Debug.Assert(pb <= (pb + numBytes));

            if (numBytes == 0)
                return Empty;

#if TARGET_WINDOWS
            int numCharsRequired = Interop.Kernel32.MultiByteToWideChar(Interop.Kernel32.CP_ACP, Interop.Kernel32.MB_PRECOMPOSED, pb, numBytes, (char*)null, 0);
            if (numCharsRequired == 0)
                throw new ArgumentException(SR.Arg_InvalidANSIString);

            string newString = FastAllocateString(numCharsRequired);
            fixed (char* pFirstChar = &newString.GetRawStringData())
            {
                numCharsRequired = Interop.Kernel32.MultiByteToWideChar(Interop.Kernel32.CP_ACP, Interop.Kernel32.MB_PRECOMPOSED, pb, numBytes, pFirstChar, numCharsRequired);
            }
            if (numCharsRequired == 0)
                throw new ArgumentException(SR.Arg_InvalidANSIString);
            return newString;
#else
            return CreateStringFromEncoding(pb, numBytes, Encoding.UTF8);
#endif
        }

        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.SByte*,System.Int32,System.Int32,System.Text.Encoding)")]
#endif
        public extern unsafe String(sbyte* value, int startIndex, int length, Encoding enc);

        private static unsafe string Ctor(sbyte* value, int startIndex, int length, Encoding? enc)
        {
            if (enc == null)
                return new string(value, startIndex, length);

            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfNegative(startIndex);

            if (value == null)
            {
                if (length == 0)
                    return Empty;

                ArgumentNullException.Throw(nameof(value));
            }

            byte* pStart = (byte*)(value + startIndex);

            // overflow check
            if (pStart < value)
                throw new ArgumentOutOfRangeException(nameof(startIndex), SR.ArgumentOutOfRange_PartialWCHAR);

            return enc.GetString(new ReadOnlySpan<byte>(pStart, length));
        }

        /// <safety>The runtime FCall builds the string from the character and count values alone; it takes no pointer and touches no caller-supplied memory.</safety>
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.Char,System.Int32)")]
#endif
        public extern String(char c, int count);

        private static string Ctor(char c, int count)
        {
            if (count <= 0)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                return Empty;
            }

            string result = FastAllocateString(count);
            if (c != '\0')
            {
                SpanHelpers.Fill(ref result.GetRawStringData(), (uint)count, c);
            }
            return result;
        }

        /// <safety>The runtime FCall copies from the bounds-checked managed span; it takes no raw pointer and touches no caller-supplied unmanaged memory.</safety>
        [MethodImpl(MethodImplOptions.InternalCall)]
#if MONO
        [DynamicDependency("Ctor(System.ReadOnlySpan{System.Char})")]
#endif
        public extern String(ReadOnlySpan<char> value);

        private static string Ctor(ReadOnlySpan<char> value)
        {
            if (value.Length == 0)
                return Empty;

            string result = FastAllocateString(value.Length);
            Buffer.Memmove(ref result.GetRawStringData(), ref MemoryMarshal.GetReference(value), (uint)value.Length);
            return result;
        }

        public static string Create<TState>(int length, TState state, SpanAction<char, TState> action)
            where TState : allows ref struct
        {
            // To support interop scenarios, the underlying buffer is guaranteed to be at least 1 greater than represented by the span parameter of the action callback.
            // This additional index represents the null-terminator and, if written, that is the only value supported.
            // Writing any value other than the null-terminator corrupts the string and is considered undefined behavior.

            if (action is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.action);
            }

            if (length <= 0)
            {
                if (length == 0)
                {
                    return Empty;
                }

                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
            }

            string result = FastAllocateString(length);
            action(new Span<char>(ref result.GetRawStringData(), length), state);
            return result;
        }

        /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="handler">The interpolated string.</param>
        /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
        public static string Create(IFormatProvider? provider, [InterpolatedStringHandlerArgument(nameof(provider))] ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        /// <summary>Creates a new string by using the specified provider to control the formatting of the specified interpolated string.</summary>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        /// <param name="initialBuffer">The initial buffer that may be used as temporary space as part of the formatting operation. The contents of this buffer may be overwritten.</param>
        /// <param name="handler">The interpolated string.</param>
        /// <returns>The string that results for formatting the interpolated string using the specified format provider.</returns>
        public static string Create(IFormatProvider? provider, Span<char> initialBuffer, [InterpolatedStringHandlerArgument(nameof(provider), nameof(initialBuffer))] ref DefaultInterpolatedStringHandler handler) =>
            handler.ToStringAndClear();

        [Intrinsic] // When input is a string literal
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ReadOnlySpan<char>(string? value) =>
            value != null ? new ReadOnlySpan<char>(ref value.GetRawStringData(), value.Length) : default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetSpan(int startIndex, int count, out ReadOnlySpan<char> slice)
        {
#if TARGET_64BIT
            // See comment in Span<T>.Slice for how this works.
            if ((ulong)(uint)startIndex + (ulong)(uint)count > (ulong)(uint)Length)
            {
                slice = default;
                return false;
            }
#else
            if ((uint)startIndex > (uint)Length || (uint)count > (uint)(Length - startIndex))
            {
                slice = default;
                return false;
            }
#endif

            slice = new ReadOnlySpan<char>(ref Unsafe.Add(ref GetRawStringData(), (nint)(uint)startIndex /* force zero-extension */), count);
            return true;
        }

        public object Clone()
        {
            return this;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("This API should not be used to create mutable strings. See https://go.microsoft.com/fwlink/?linkid=2084035 for alternatives.")]
        public static string Copy(string str)
        {
            ArgumentNullException.ThrowIfNull(str);

            string result = FastAllocateString(str.Length);

            Buffer.Memmove(
                elementCount: (uint)result.Length, // derefing Length now allows JIT to prove 'result' not null below
                destination: ref result.GetRawStringData(),
                source: ref str.GetRawStringData());

            return result;
        }

        // Converts a substring of this string to an array of characters.  Copies the
        // characters of this string beginning at position sourceIndex and ending at
        // sourceIndex + count - 1 to the character array buffer, beginning
        // at destinationIndex.
        //
        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            ArgumentNullException.ThrowIfNull(destination);

            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length - sourceIndex, nameof(sourceIndex));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(destinationIndex, destination.Length - count);
            ArgumentOutOfRangeException.ThrowIfNegative(destinationIndex);

            Buffer.Memmove(
                destination: ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(destination), destinationIndex),
                source: ref Unsafe.Add(ref GetRawStringData(), sourceIndex),
                elementCount: (uint)count);
        }

        /// <summary>Copies the contents of this string into the destination span.</summary>
        /// <param name="destination">The span into which to copy this string's contents.</param>
        /// <exception cref="ArgumentException">The destination span is shorter than the source string.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(Span<char> destination)
        {
            if ((uint)Length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref GetRawStringData(), (uint)Length);
            }
            else
            {
                ThrowHelper.ThrowArgumentException_DestinationTooShort();
            }
        }

        /// <summary>Copies the contents of this string into the destination span.</summary>
        /// <param name="destination">The span into which to copy this string's contents.</param>
        /// <returns>true if the data was copied; false if the destination was too short to fit the contents of the string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCopyTo(Span<char> destination)
        {
            bool retVal = false;
            if ((uint)Length <= (uint)destination.Length)
            {
                Buffer.Memmove(ref destination._reference, ref GetRawStringData(), (uint)Length);
                retVal = true;
            }
            return retVal;
        }

        // Returns the entire string as an array of characters.
        public char[] ToCharArray()
        {
            if (Length == 0)
                return [];

            char[] chars = new char[Length];

            Buffer.Memmove(
                destination: ref MemoryMarshal.GetArrayDataReference(chars),
                source: ref GetRawStringData(),
                elementCount: (uint)Length);

            return chars;
        }

        // Returns a substring of this string as an array of characters.
        //
        public char[] ToCharArray(int startIndex, int length)
        {
            // Range check everything.
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)startIndex, (uint)Length, nameof(startIndex));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, Length - length);

            if (length <= 0)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(length);
                return [];
            }

            char[] chars = new char[length];

            Buffer.Memmove(
               destination: ref MemoryMarshal.GetArrayDataReference(chars),
               source: ref Unsafe.Add(ref GetRawStringData(), startIndex),
               elementCount: (uint)length);

            return chars;
        }

        public static bool IsNullOrEmpty([NotNullWhen(false)] string? value)
        {
            return value == null || value.Length == 0;
        }

        public static bool IsNullOrWhiteSpace([NotNullWhen(false)] string? value)
        {
            if (value == null) return true;

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a reference to the first element of the String. If the string is null, an access will throw a NullReferenceException.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [NonVersionable]
        public ref readonly char GetPinnableReference() => ref GetRawStringData();
        internal ref byte GetRawStringDataAsUInt8() => ref Unsafe.As<char, byte>(ref GetRawStringData());
        internal ref ushort GetRawStringDataAsUInt16() => ref Unsafe.As<char, ushort>(ref GetRawStringData());

        // Helper for encodings so they can talk to our buffer directly
        // stringLength must be the exact size we'll expect
        internal static unsafe string CreateStringFromEncoding(
            byte* bytes, int byteLength, Encoding encoding)
        {
            Debug.Assert(bytes != null);
            Debug.Assert(byteLength >= 0);

            // Get our string length
            int stringLength = encoding.GetCharCount(bytes, byteLength);
            Debug.Assert(stringLength >= 0);

            // They gave us an empty string if they needed one
            // 0 bytelength might be possible if there's something in an encoder
            if (stringLength == 0)
                return Empty;

            string s = FastAllocateString(stringLength);
            fixed (char* pTempChars = &s.GetRawStringData())
            {
                int doubleCheck = encoding.GetChars(bytes, byteLength, pTempChars, stringLength);
                Debug.Assert(stringLength == doubleCheck,
                    "Expected encoding.GetChars to return same length as encoding.GetCharCount");
            }

            return s;
        }

        // This is only intended to be used by char.ToString.
        // It is necessary to put the code in this class instead of Char, since GetRawStringData() is a private member.
        // Making GetRawStringData() internal would be dangerous since it would make it much easier to break String's immutability.
        internal static string CreateFromChar(char c)
        {
            string result = FastAllocateString(1);
            result.GetRawStringData() = c;
            return result;
        }

        internal static string CreateFromChar(char c1, char c2)
        {
            string result = FastAllocateString(2);
            result.GetRawStringData() = c1;
            Unsafe.Add(ref result.GetRawStringData(), 1) = c2;
            return result;
        }

        // Returns this string.
        public override string ToString()
        {
            return this;
        }

        // Returns this string.
        public string ToString(IFormatProvider? provider)
        {
            return this;
        }

        public CharEnumerator GetEnumerator()
        {
            return new CharEnumerator(this);
        }

        IEnumerator<char> IEnumerable<char>.GetEnumerator()
        {
            return new CharEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new CharEnumerator(this);
        }

        /// <summary>
        /// Returns an enumeration of <see cref="Rune"/> from this string.
        /// </summary>
        /// <remarks>
        /// Invalid sequences will be represented in the enumeration by <see cref="Rune.ReplacementChar"/>.
        /// </remarks>
        public StringRuneEnumerator EnumerateRunes()
        {
            return new StringRuneEnumerator(this);
        }

        internal static unsafe int wcslen(char* ptr) => SpanHelpers.IndexOfNullCharacter(ptr);

        internal static unsafe int strlen(byte* ptr) => SpanHelpers.IndexOfNullByte(ptr);

        //
        // IConvertible implementation
        //

        public TypeCode GetTypeCode()
        {
            return TypeCode.String;
        }

        bool IConvertible.ToBoolean(IFormatProvider? provider)
        {
            return Convert.ToBoolean(this, provider);
        }

        char IConvertible.ToChar(IFormatProvider? provider)
        {
            return Convert.ToChar(this, provider);
        }

        sbyte IConvertible.ToSByte(IFormatProvider? provider)
        {
            return Convert.ToSByte(this, provider);
        }

        byte IConvertible.ToByte(IFormatProvider? provider)
        {
            return Convert.ToByte(this, provider);
        }

        short IConvertible.ToInt16(IFormatProvider? provider)
        {
            return Convert.ToInt16(this, provider);
        }

        ushort IConvertible.ToUInt16(IFormatProvider? provider)
        {
            return Convert.ToUInt16(this, provider);
        }

        int IConvertible.ToInt32(IFormatProvider? provider)
        {
            return Convert.ToInt32(this, provider);
        }

        uint IConvertible.ToUInt32(IFormatProvider? provider)
        {
            return Convert.ToUInt32(this, provider);
        }

        long IConvertible.ToInt64(IFormatProvider? provider)
        {
            return Convert.ToInt64(this, provider);
        }

        ulong IConvertible.ToUInt64(IFormatProvider? provider)
        {
            return Convert.ToUInt64(this, provider);
        }

        float IConvertible.ToSingle(IFormatProvider? provider)
        {
            return Convert.ToSingle(this, provider);
        }

        double IConvertible.ToDouble(IFormatProvider? provider)
        {
            return Convert.ToDouble(this, provider);
        }

        decimal IConvertible.ToDecimal(IFormatProvider? provider)
        {
            return Convert.ToDecimal(this, provider);
        }

        DateTime IConvertible.ToDateTime(IFormatProvider? provider)
        {
            return Convert.ToDateTime(this, provider);
        }

        object IConvertible.ToType(Type type, IFormatProvider? provider)
        {
            return Convert.DefaultToType((IConvertible)this, type, provider);
        }

        // Normalization Methods
        // These just wrap calls to Normalization class
        public bool IsNormalized()
        {
            return IsNormalized(NormalizationForm.FormC);
        }

        public bool IsNormalized(NormalizationForm normalizationForm)
        {
            return Normalization.IsNormalized(this, normalizationForm);
        }

        public string Normalize()
        {
            return Normalize(NormalizationForm.FormC);
        }

        public string Normalize(NormalizationForm normalizationForm)
        {
            return Normalization.Normalize(this, normalizationForm);
        }

        //
        // IParsable
        //

        static string IParsable<string>.Parse(string s, IFormatProvider? provider)
        {
            ArgumentNullException.ThrowIfNull(s);
            return s;
        }

        static bool IParsable<string>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out string result)
        {
            result = s;
            return s is not null;
        }

        //
        // ISpanParsable
        //

        static string ISpanParsable<string>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        {
            if (s.Length > MaxLength)
            {
                ThrowHelper.ThrowFormatInvalidString();
            }
            return s.ToString();
        }

        static bool ISpanParsable<string>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out string result)
        {
            if (s.Length <= MaxLength)
            {
                result = s.ToString();
                return true;
            }
            else
            {
                result = null;
                return false;
            }
        }
    }
}
