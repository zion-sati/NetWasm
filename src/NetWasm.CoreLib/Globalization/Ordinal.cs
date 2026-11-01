// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de src/libraries/System.Private.CoreLib/src/System/Globalization/Ordinal.cs.
// NetWasm always runs upstream's invariant globalization mode, so this copy leaves out the NLS and ICU
// casing paths invariant mode never takes: each method ends at its invariant-mode branch, and the
// NLS-only helpers are gone.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Unicode;

namespace System.Globalization
{
    internal static partial class Ordinal
    {
        internal static int CompareStringIgnoreCase(ref char strA, int lengthA, ref char strB, int lengthB)
        {
            int length = Math.Min(lengthA, lengthB);
            int range = length;

            ref char charA = ref strA;
            ref char charB = ref strB;

            const char maxChar = (char)0x7F;

            while (length != 0 && charA <= maxChar && charB <= maxChar)
            {
                // Ordinal equals or lowercase equals if the result ends up in the a-z range
                if (charA == charB ||
                    ((charA | 0x20) == (charB | 0x20) && char.IsAsciiLetter(charA)))
                {
                    length--;
                    charA = ref Unsafe.Add(ref charA, 1);
                    charB = ref Unsafe.Add(ref charB, 1);
                }
                else
                {
                    int currentA = charA;
                    int currentB = charB;

                    // Uppercase both chars if needed
                    if (char.IsAsciiLetterLower(charA))
                    {
                        currentA -= 0x20;
                    }
                    if (char.IsAsciiLetterLower(charB))
                    {
                        currentB -= 0x20;
                    }

                    // Return the (case-insensitive) difference between them.
                    return currentA - currentB;
                }
            }

            if (length == 0)
            {
                return lengthA - lengthB;
            }

            range -= length;

            return CompareStringIgnoreCaseNonAscii(ref charA, lengthA - range, ref charB, lengthB - range);
        }

        internal static int CompareStringIgnoreCaseNonAscii(ref char strA, int lengthA, ref char strB, int lengthB)
        {
            return InvariantModeCasing.CompareStringIgnoreCase(ref strA, lengthA, ref strB, lengthB);
        }

        private static bool EqualsIgnoreCase_Vector<TVector>(ref char charA, ref char charB, int length)
            where TVector : struct, ISimdVector<TVector, ushort>
        {
            Debug.Assert(length >= TVector.ElementCount);

            nuint lengthU = (nuint)length;
            nuint lengthToExamine = lengthU - (nuint)TVector.ElementCount;
            nuint i = 0;
            TVector vec1;
            TVector vec2;
            TVector loweringMask = TVector.Create(0x20);
            TVector vecA = TVector.Create('a');
            TVector vecZMinusA = TVector.Create('z' - 'a');
            do
            {
                vec1 = TVector.LoadUnsafe(ref Unsafe.As<char, ushort>(ref charA), i);
                vec2 = TVector.LoadUnsafe(ref Unsafe.As<char, ushort>(ref charB), i);

                if (!Utf16Utility.AllCharsInVectorAreAscii(vec1 | vec2))
                {
                    goto NON_ASCII;
                }

                TVector notEquals = ~TVector.Equals(vec1, vec2);
                if (!notEquals.Equals(TVector.Zero))
                {
                    // not exact match

                    vec1 |= loweringMask;
                    vec2 |= loweringMask;
                    if (TVector.GreaterThanAny((vec1 - vecA) & notEquals, vecZMinusA) || !vec1.Equals(vec2))
                    {
                        return false; // first input isn't in [A-Za-z], and not exact match of lowered
                    }
                }
                i += (nuint)TVector.ElementCount;
            } while (i <= lengthToExamine);

            // Handle trailing elements
            if (i != lengthU)
            {
                i = lengthU - (nuint)TVector.ElementCount;
                vec1 = TVector.LoadUnsafe(ref Unsafe.As<char, ushort>(ref charA), i);
                vec2 = TVector.LoadUnsafe(ref Unsafe.As<char, ushort>(ref charB), i);

                if (!Utf16Utility.AllCharsInVectorAreAscii(vec1 | vec2))
                {
                    goto NON_ASCII;
                }

                TVector notEquals = ~TVector.Equals(vec1, vec2);
                if (!notEquals.Equals(TVector.Zero))
                {
                    // not exact match

                    vec1 |= loweringMask;
                    vec2 |= loweringMask;
                    if (TVector.GreaterThanAny((vec1 - vecA) & notEquals, vecZMinusA) || !vec1.Equals(vec2))
                    {
                        return false; // first input isn't in [A-Za-z], and not exact match of lowered
                    }
                }
            }
            return true;

        NON_ASCII:
            if (Utf16Utility.AllCharsInVectorAreAscii(vec1) || Utf16Utility.AllCharsInVectorAreAscii(vec2))
            {
                // No need to use the fallback if one of the inputs is full-ASCII
                return false;
            }

            // Fallback for Non-ASCII inputs
            return CompareStringIgnoreCase(
                ref Unsafe.Add(ref charA, i), (int)(lengthU - i),
                ref Unsafe.Add(ref charB, i), (int)(lengthU - i)) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EqualsIgnoreCase(ref char charA, ref char charB, int length)
        {
            if (!Vector128.IsHardwareAccelerated || length < Vector128<ushort>.Count)
            {
                return EqualsIgnoreCase_Scalar(ref charA, ref charB, length);
            }
            if (Vector512.IsHardwareAccelerated && length >= Vector512<ushort>.Count)
            {
                return EqualsIgnoreCase_Vector<Vector512<ushort>>(ref charA, ref charB, length);
            }
            if (Vector256.IsHardwareAccelerated && length >= Vector256<ushort>.Count)
            {
                return EqualsIgnoreCase_Vector<Vector256<ushort>>(ref charA, ref charB, length);
            }
            return EqualsIgnoreCase_Vector<Vector128<ushort>>(ref charA, ref charB, length);
        }

        internal static bool EqualsIgnoreCase_Scalar(ref char charA, ref char charB, int length)
        {
            IntPtr byteOffset = IntPtr.Zero;

#if TARGET_64BIT
            ulong valueAu64 = 0;
            ulong valueBu64 = 0;
            // Read 4 chars (64 bits) at a time from each string
            while ((uint)length >= 4)
            {
                valueAu64 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref charA, byteOffset)));
                valueBu64 = Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref charB, byteOffset)));

                // A 32-bit test - even with the bit-twiddling here - is more efficient than a 64-bit test.
                ulong temp = valueAu64 | valueBu64;
                if (!Utf16Utility.AllCharsInUInt32AreAscii((uint)temp | (uint)(temp >> 32)))
                {
                    goto NonAscii64; // one of the inputs contains non-ASCII data
                }

                // Generally, the caller has likely performed a first-pass check that the input strings
                // are likely equal. Consider a dictionary which computes the hash code of its key before
                // performing a proper deep equality check of the string contents. We want to optimize for
                // the case where the equality check is likely to succeed, which means that we want to avoid
                // branching within this loop unless we're about to exit the loop, either due to failure or
                // due to us running out of input data.

                if (!Utf16Utility.UInt64OrdinalIgnoreCaseAscii(valueAu64, valueBu64))
                {
                    return false;
                }

                byteOffset += 8;
                length -= 4;
            }
#endif
            uint valueAu32 = 0;
            uint valueBu32 = 0;
            // Read 2 chars (32 bits) at a time from each string
#if TARGET_64BIT
            if ((uint)length >= 2)
#else
            while ((uint)length >= 2)
#endif
            {
                valueAu32 = Unsafe.ReadUnaligned<uint>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref charA, byteOffset)));
                valueBu32 = Unsafe.ReadUnaligned<uint>(ref Unsafe.As<char, byte>(ref Unsafe.AddByteOffset(ref charB, byteOffset)));

                if (!Utf16Utility.AllCharsInUInt32AreAscii(valueAu32 | valueBu32))
                {
                    goto NonAscii32; // one of the inputs contains non-ASCII data
                }

                // Generally, the caller has likely performed a first-pass check that the input strings
                // are likely equal. Consider a dictionary which computes the hash code of its key before
                // performing a proper deep equality check of the string contents. We want to optimize for
                // the case where the equality check is likely to succeed, which means that we want to avoid
                // branching within this loop unless we're about to exit the loop, either due to failure or
                // due to us running out of input data.

                if (!Utf16Utility.UInt32OrdinalIgnoreCaseAscii(valueAu32, valueBu32))
                {
                    return false;
                }

                byteOffset += 4;
                length -= 2;
            }

            if (length != 0)
            {
                Debug.Assert(length == 1);

                valueAu32 = Unsafe.AddByteOffset(ref charA, byteOffset);
                valueBu32 = Unsafe.AddByteOffset(ref charB, byteOffset);

                if ((valueAu32 | valueBu32) > 0x7Fu)
                {
                    goto NonAscii32; // one of the inputs contains non-ASCII data
                }

                if (valueAu32 == valueBu32)
                {
                    return true; // exact match
                }

                valueAu32 |= 0x20u;
                if ((uint)(valueAu32 - 'a') > (uint)('z' - 'a'))
                {
                    return false; // not exact match, and first input isn't in [A-Za-z]
                }

                return valueAu32 == (valueBu32 | 0x20u);
            }

            Debug.Assert(length == 0);
            return true;

        NonAscii32:
            // Both values have to be non-ASCII to use the slow fallback, in case if one of them is not we return false
            if (Utf16Utility.AllCharsInUInt32AreAscii(valueAu32) || Utf16Utility.AllCharsInUInt32AreAscii(valueBu32))
            {
                return false;
            }
            goto NonAscii;

#if TARGET_64BIT
        NonAscii64:
            // Both values have to be non-ASCII to use the slow fallback, in case if one of them is not we return false
            if (Utf16Utility.AllCharsInUInt64AreAscii(valueAu64) || Utf16Utility.AllCharsInUInt64AreAscii(valueBu64))
            {
                return false;
            }
#endif
        NonAscii:
            // The non-ASCII case is factored out into its own helper method so that the JIT
            // doesn't need to emit a complex prolog for its caller (this method).
            return CompareStringIgnoreCase(ref Unsafe.AddByteOffset(ref charA, byteOffset), length, ref Unsafe.AddByteOffset(ref charB, byteOffset), length) == 0;
        }

        internal static int IndexOf(string source, string value, int startIndex, int count, bool ignoreCase)
        {
            if (source == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (value == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.value);
            }

            if (!source.TryGetSpan(startIndex, count, out ReadOnlySpan<char> sourceSpan))
            {
                // Bounds check failed - figure out exactly what went wrong so that we can
                // surface the correct argument exception.

                if ((uint)startIndex > (uint)source.Length)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.startIndex, ExceptionResource.ArgumentOutOfRange_IndexMustBeLessOrEqual);
                }
                else
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count, ExceptionResource.ArgumentOutOfRange_Count);
                }
            }

            int result = ignoreCase ? IndexOfOrdinalIgnoreCase(sourceSpan, value) : sourceSpan.IndexOf(value);

            return result >= 0 ? result + startIndex : result;
        }

        internal static int IndexOfOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
        {
            if (value.Length == 0)
            {
                return 0;
            }

            if (value.Length > source.Length)
            {
                // A non-linguistic search compares chars directly against one another, so large
                // target strings can never be found inside small search spaces. This check also
                // handles empty 'source' spans.
                return -1;
            }

            return InvariantModeCasing.IndexOfIgnoreCase(source, value);
        }

        internal static int LastIndexOf(string source, string value, int startIndex, int count)
        {
            int result = source.AsSpan(startIndex, count).LastIndexOf(value);
            if (result >= 0) { result += startIndex; } // if match found, adjust 'result' by the actual start position
            return result;
        }

        internal static int LastIndexOf(string source, string value, int startIndex, int count, bool ignoreCase)
        {
            if (source == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
            }

            if (value == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.value);
            }

            if (value.Length == 0)
            {
                return startIndex + 1; // startIndex is the index of the last char to include in the search space
            }

            if (count == 0)
            {
                return -1;
            }

            return ignoreCase ? InvariantModeCasing.LastIndexOfIgnoreCase(source.AsSpan(startIndex, count), value) : LastIndexOf(source, value, startIndex, count);
        }

        internal static int LastIndexOfOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
        {
            if (value.Length == 0)
            {
                return source.Length;
            }

            if (value.Length > source.Length)
            {
                // A non-linguistic search compares chars directly against one another, so large
                // target strings can never be found inside small search spaces. This check also
                // handles empty 'source' spans.

                return -1;
            }

            return InvariantModeCasing.LastIndexOfIgnoreCase(source, value);
        }

        internal static int ToUpperOrdinal(ReadOnlySpan<char> source, Span<char> destination)
        {
            if (source.Overlaps(destination))
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_SpanOverlappedOperation);

            // Assuming that changing case does not affect length
            if (destination.Length < source.Length)
                return -1;

            InvariantModeCasing.ToUpper(source, destination);
            return source.Length;
        }

        internal static int ToLowerOrdinal(ReadOnlySpan<char> source, Span<char> destination)
        {
            if (source.Overlaps(destination))
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_SpanOverlappedOperation);

            // Assuming that changing case does not affect length
            if (destination.Length < source.Length)
                return -1;

            InvariantModeCasing.ToLower(source, destination);
            PreserveOrdinalLowerCasingClass(source, destination);
            return source.Length;
        }

        // The only BMP scalars whose simple invariant/NLS lower mapping moves them out of their ordinal
        // upper-casing class. Each one is its own ordinal upper-casing (ToUpperOrdinal(c) == c) yet lowers to a
        // different letter, so simple lowering would break OrdinalIgnoreCase consistency for them. The ICU ordinal
        // table already keeps them unchanged; invariant and NLS lowering do not, so they must be restored.
        // U+03F4 GREEK CAPITAL THETA SYMBOL, U+1E9E LATIN CAPITAL LETTER SHARP S, U+2126 OHM SIGN,
        // U+212A KELVIN SIGN, U+212B ANGSTROM SIGN. The ToLowerOrdinal_EveryBmpChar tests would fail if this set
        // ever became incomplete.
        private static readonly SearchValues<char> s_ordinalLowerCasingClassRestoreChars =
            SearchValues.Create("\u03F4\u1E9E\u2126\u212A\u212B");

        // Ordinal lower casing must never move a character out of its ordinal upper-casing class, otherwise it
        // would stop being consistent with OrdinalIgnoreCase (for example the Kelvin, Ohm and Angstrom signs).
        // Only the fixed set above is ever affected, so skip the pass entirely unless one of them is present.
        private static void PreserveOrdinalLowerCasingClass(ReadOnlySpan<char> source, Span<char> destination)
        {
            int i = source.IndexOfAny(s_ordinalLowerCasingClassRestoreChars);
            if (i < 0)
            {
                return;
            }

            // Restore each affected character to its original form; its ordinal lower casing is itself.
            for (; i < source.Length; i++)
            {
                if (s_ordinalLowerCasingClassRestoreChars.Contains(source[i]))
                {
                    destination[i] = source[i];
                }
            }
        }
    }
}
