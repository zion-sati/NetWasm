// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de: the members of
// src/libraries/System.Private.CoreLib/src/System/String.Manipulation.cs, String.Comparison.cs and
// String.cs that MemoryExtensions and Ordinal use, which NetWasm's own string does not define.

using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace System
{
    public partial class String
    {
        // Avoid paying the init cost of all the SearchValues unless they are actually used.
        internal static class SearchValuesStorage
        {
            /// <summary>
            /// SearchValues would use SpanHelpers.IndexOfAnyValueType for 5 values in this case.
            /// No need to allocate the SearchValues as a regular Span.IndexOfAny will use the same implementation.
            /// </summary>
            public const string NewLineCharsExceptLineFeed = "\r\f\u0085\u2028\u2029";

            /// <summary>
            /// The Unicode Standard, Sec. 5.8, Recommendation R4 and Table 5-2 state that the CR, LF,
            /// CRLF, NEL, LS, FF, and PS sequences are considered newline functions. That section
            /// also specifically excludes VT from the list of newline functions, so we do not include
            /// it in the needle list.
            /// </summary>
            public static readonly SearchValues<char> NewLineChars =
                SearchValues.Create(NewLineCharsExceptLineFeed + "\n");

            /// <summary>A <see cref="SearchValues{Char}"/> for all of the Unicode whitespace characters</summary>
            public static readonly SearchValues<char> WhiteSpaceChars =
                SearchValues.Create("\t\n\v\f\r\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000");

            // All ASCII characters except the lowercase letters 'a'-'z'. The first character outside this set is
            // the first one ToUpperOrdinal must change (a lowercase ASCII letter or a non-ASCII character).
            public static readonly SearchValues<char> AsciiExceptLowercase =
                SearchValues.Create("\0\u0001\u0002\u0003\u0004\u0005\u0006\a\b\t\n\v\f\r\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\e\u001C\u001D\u001E\u001F !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`{|}~\u007F");

            // All ASCII characters except the uppercase letters 'A'-'Z'. The first character outside this set is
            // the first one ToLowerOrdinal must change (an uppercase ASCII letter or a non-ASCII character).
            public static readonly SearchValues<char> AsciiExceptUppercase =
                SearchValues.Create("\0\u0001\u0002\u0003\u0004\u0005\u0006\a\b\t\n\v\f\r\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\e\u001C\u001D\u001E\u001F !\"#$%&'()*+,-./0123456789:;<=>?@[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~\u007F");

#if DEBUG
            static SearchValuesStorage()
            {
                SearchValues<char> sv = WhiteSpaceChars;
                for (int i = 0; i <= char.MaxValue; i++)
                {
                    Debug.Assert(char.IsWhiteSpace((char)i) == sv.Contains((char)i));
                }
            }
#endif
        }

        internal const int StackallocIntBufferSizeLimit = 128;
        internal const int StackallocCharBufferSizeLimit = 256;

        /// <summary>
        /// Uses ValueListBuilder to create list that holds indexes of separators in string.
        /// </summary>
        /// <param name="source">The source to parse.</param>
        /// <param name="separators"><see cref="ReadOnlySpan{T}"/> of separator chars</param>
        /// <param name="sepListBuilder"><see cref="ValueListBuilder{T}"/> to store indexes</param>
        internal static void MakeSeparatorListAny(ReadOnlySpan<char> source, ReadOnlySpan<char> separators, ref ValueListBuilder<int> sepListBuilder)
        {
            // Special-case no separators to mean any whitespace is a separator.
            if (separators.Length == 0)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    if (char.IsWhiteSpace(source[i]))
                    {
                        sepListBuilder.Append(i);
                    }
                }
            }

            // Special-case the common cases of 1, 2, and 3 separators, with manual comparisons against each separator.
            else if (separators.Length <= 3)
            {
                char sep0, sep1, sep2;
                sep0 = separators[0];
                sep1 = separators.Length > 1 ? separators[1] : sep0;
                sep2 = separators.Length > 2 ? separators[2] : sep1;
                if (Vector128.IsHardwareAccelerated && source.Length >= Vector128<ushort>.Count * 2)
                {
                    MakeSeparatorListVectorized(source, ref sepListBuilder, sep0, sep1, sep2);
                    return;
                }

                for (int i = 0; i < source.Length; i++)
                {
                    char c = source[i];
                    if (c == sep0 || c == sep1 || c == sep2)
                    {
                        sepListBuilder.Append(i);
                    }
                }
            }

            // Handle > 3 separators with a probabilistic map, ala IndexOfAny.
            // This optimizes for chars being unlikely to match a separator.
            else
            {
                var map = new ProbabilisticMap(separators);
                ref uint charMap = ref Unsafe.As<ProbabilisticMap, uint>(ref map);

                for (int i = 0; i < source.Length; i++)
                {
                    if (ProbabilisticMap.Contains(ref charMap, separators, source[i]))
                    {
                        sepListBuilder.Append(i);
                    }
                }
            }
        }

        private static void MakeSeparatorListVectorized(ReadOnlySpan<char> sourceSpan, ref ValueListBuilder<int> sepListBuilder, char c, char c2, char c3)
        {
            // Redundant test so we won't prejit remainder of this method
            // on platforms where it is not supported
            if (!Vector128.IsHardwareAccelerated)
            {
                throw new PlatformNotSupportedException();
            }
            Debug.Assert(sourceSpan.Length >= Vector128<ushort>.Count * 2);
            int baseIndex = 0;
            ReadOnlySpan<ushort> sourceSpanUInt16 = MemoryMarshal.Cast<char, ushort>(sourceSpan);
            ReadOnlySpan<ushort> remaining = sourceSpanUInt16;

            if (Vector512.IsHardwareAccelerated && (uint)remaining.Length >= (uint)Vector512<ushort>.Count * 2)
            {
                Vector512<ushort> v1 = Vector512.Create((ushort)c);
                Vector512<ushort> v2 = Vector512.Create((ushort)c2);
                Vector512<ushort> v3 = Vector512.Create((ushort)c3);

                if (Avx512BW.IsSupported && PackedSpanHelpers.CanUsePackedIndexOf(c) && PackedSpanHelpers.CanUsePackedIndexOf(c2) && PackedSpanHelpers.CanUsePackedIndexOf(c3))
                {
                    // Process in double chunks & check if either chunk is likely to contain matches at once.
                    // If we get multiple matches in a single chunk, we assume they're likely to be close &
                    // break out of this logic & use the more optimistic loop below.
                    // This is similar logic to SpanHelpers.Packed.cs's IndexOf.
                    Vector512<byte> packedComparand1 = Vector512.Create((byte)c);
                    Vector512<byte> packedComparand2 = Vector512.Create((byte)c2);
                    Vector512<byte> packedComparand3 = Vector512.Create((byte)c3);
                    while ((uint)remaining.Length >= (uint)Vector512<ushort>.Count * 2)
                    {
                        Vector512<ushort> vector1 = Vector512.Create(remaining);
                        Vector512<ushort> vector2 = Vector512.Create(remaining.Slice(Vector512<ushort>.Count));
                        var packed = PackedSpanHelpers.PackSources(vector1.AsInt16(), vector2.AsInt16());

                        if ((Vector512.Equals(packed, packedComparand1) | Vector512.Equals(packed, packedComparand2) | Vector512.Equals(packed, packedComparand3)) != Vector512<byte>.Zero)
                        {
                            var cmp1 = Vector512.Equals(vector1, v1).AsByte() | Vector512.Equals(vector1, v2).AsByte() | Vector512.Equals(vector1, v3).AsByte();
                            var cmp2 = Vector512.Equals(vector2, v1).AsByte() | Vector512.Equals(vector2, v2).AsByte() | Vector512.Equals(vector2, v3).AsByte();

                            // Same logic as below, but for both vectors.
                            ulong mask1 = cmp1.ExtractMostSignificantBits() & 0x5555555555555555;
                            ulong mask2 = cmp2.ExtractMostSignificantBits() & 0x5555555555555555;
                            bool shouldBreak = ulong.PopCount(mask1) + ulong.PopCount(mask2) > 1;
                            while (mask1 != 0)
                            {
                                uint bitPos = (uint)BitOperations.TrailingZeroCount(mask1) / sizeof(char);
                                sepListBuilder.Append(baseIndex + (int)bitPos);
                                mask1 = BitOperations.ResetLowestSetBit(mask1);
                            }
                            while (mask2 != 0)
                            {
                                uint bitPos = (uint)BitOperations.TrailingZeroCount(mask2) / sizeof(char);
                                sepListBuilder.Append(baseIndex + (int)bitPos + Vector512<ushort>.Count);
                                mask2 = BitOperations.ResetLowestSetBit(mask2);
                            }

                            // Break out of the loop if we had >1 match:
                            if (shouldBreak)
                            {
                                baseIndex += Vector512<ushort>.Count * 2;
                                remaining = remaining.Slice(Vector512<ushort>.Count * 2);
                                break;
                            }
                        }

                        baseIndex += Vector512<ushort>.Count * 2;
                        remaining = remaining.Slice(Vector512<ushort>.Count * 2);
                    }
                }

                while ((uint)remaining.Length >= (uint)Vector512<ushort>.Count)
                {
                    Vector512<ushort> vector = Vector512.Create(remaining);
                    Vector512<byte> cmp = Vector512.Equals(vector, v1).AsByte() | Vector512.Equals(vector, v2).AsByte() | Vector512.Equals(vector, v3).AsByte();

                    if (cmp != Vector512<byte>.Zero)
                    {
                        // Skip every other bit
                        ulong mask = cmp.ExtractMostSignificantBits() & 0x5555555555555555;
                        do
                        {
                            uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
                            sepListBuilder.Append(baseIndex + (int)bitPos);
                            mask = BitOperations.ResetLowestSetBit(mask);
                        } while (mask != 0);
                    }

                    baseIndex += Vector512<ushort>.Count;
                    remaining = remaining.Slice(Vector512<ushort>.Count);
                }

                // Handle the last chunk in a vectorized way also.
                // We do a whole vector's worth again, but just mask out the bits we've already handled.
                if (remaining.Length > 0)
                {
                    Vector512<ushort> vector = Vector512.Create(sourceSpanUInt16.Slice(sourceSpanUInt16.Length - Vector512<ushort>.Count));
                    Vector512<byte> cmp = Vector512.Equals(vector, v1).AsByte() | Vector512.Equals(vector, v2).AsByte() | Vector512.Equals(vector, v3).AsByte();
                    int finalIndex = sourceSpanUInt16.Length - Vector512<ushort>.Count;
                    ulong mask = cmp.ExtractMostSignificantBits() & 0x5555555555555555 & ~((1UL << (Vector512<byte>.Count - remaining.Length * sizeof(char))) - 1);
                    while (mask != 0)
                    {
                        uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
                        sepListBuilder.Append(finalIndex + (int)bitPos);
                        mask = BitOperations.ResetLowestSetBit(mask);
                    }
                }
                return;
            }
            else if (Vector256.IsHardwareAccelerated && (uint)remaining.Length >= (uint)Vector256<ushort>.Count * 2)
            {
                Vector256<ushort> v1 = Vector256.Create((ushort)c);
                Vector256<ushort> v2 = Vector256.Create((ushort)c2);
                Vector256<ushort> v3 = Vector256.Create((ushort)c3);

                if (Avx2.IsSupported && PackedSpanHelpers.CanUsePackedIndexOf(c) && PackedSpanHelpers.CanUsePackedIndexOf(c2) && PackedSpanHelpers.CanUsePackedIndexOf(c3))
                {
                    // Process in double chunks & check if either chunk is likely to contain matches at once.
                    // If we get multiple matches in a single chunk, we assume they're likely to be close &
                    // break out of this logic & use the more optimistic loop below.
                    // This is similar logic to SpanHelpers.Packed.cs's IndexOf.
                    Vector256<byte> packedComparand1 = Vector256.Create((byte)c);
                    Vector256<byte> packedComparand2 = Vector256.Create((byte)c2);
                    Vector256<byte> packedComparand3 = Vector256.Create((byte)c3);
                    while ((uint)remaining.Length >= (uint)Vector256<ushort>.Count * 2)
                    {
                        Vector256<ushort> vector1 = Vector256.Create(remaining);
                        Vector256<ushort> vector2 = Vector256.Create(remaining.Slice(Vector256<ushort>.Count));
                        var packed = PackedSpanHelpers.PackSources(vector1.AsInt16(), vector2.AsInt16());

                        if ((Vector256.Equals(packed, packedComparand1) | Vector256.Equals(packed, packedComparand2) | Vector256.Equals(packed, packedComparand3)) != Vector256<byte>.Zero)
                        {
                            var cmp1 = Vector256.Equals(vector1, v1).AsByte() | Vector256.Equals(vector1, v2).AsByte() | Vector256.Equals(vector1, v3).AsByte();
                            var cmp2 = Vector256.Equals(vector2, v1).AsByte() | Vector256.Equals(vector2, v2).AsByte() | Vector256.Equals(vector2, v3).AsByte();

                            // Same logic as below, but for both vectors.
                            uint mask1 = cmp1.ExtractMostSignificantBits() & 0x55555555;
                            uint mask2 = cmp2.ExtractMostSignificantBits() & 0x55555555;
                            bool shouldBreak = uint.PopCount(mask1) + uint.PopCount(mask2) > 1;
                            while (mask1 != 0)
                            {
                                uint bitPos = (uint)BitOperations.TrailingZeroCount(mask1) / sizeof(char);
                                sepListBuilder.Append(baseIndex + (int)bitPos);
                                mask1 = BitOperations.ResetLowestSetBit(mask1);
                            }
                            while (mask2 != 0)
                            {
                                uint bitPos = (uint)BitOperations.TrailingZeroCount(mask2) / sizeof(char);
                                sepListBuilder.Append(baseIndex + (int)bitPos + Vector256<ushort>.Count);
                                mask2 = BitOperations.ResetLowestSetBit(mask2);
                            }

                            // Break out of the loop if we had > 1 match:
                            if (shouldBreak)
                            {
                                baseIndex += Vector256<ushort>.Count * 2;
                                remaining = remaining.Slice(Vector256<ushort>.Count * 2);
                                break;
                            }
                        }

                        baseIndex += Vector256<ushort>.Count * 2;
                        remaining = remaining.Slice(Vector256<ushort>.Count * 2);
                    }
                }

                while ((uint)remaining.Length >= (uint)Vector256<ushort>.Count)
                {
                    Vector256<ushort> vector = Vector256.Create(remaining);
                    Vector256<byte> cmp = Vector256.Equals(vector, v1).AsByte() | Vector256.Equals(vector, v2).AsByte() | Vector256.Equals(vector, v3).AsByte();

                    if (cmp != Vector256<byte>.Zero)
                    {
                        // Skip every other bit
                        uint mask = cmp.ExtractMostSignificantBits() & 0x55555555;
                        do
                        {
                            uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
                            sepListBuilder.Append(baseIndex + (int)bitPos);
                            mask = BitOperations.ResetLowestSetBit(mask);
                        } while (mask != 0);
                    }

                    baseIndex += Vector256<ushort>.Count;
                    remaining = remaining.Slice(Vector256<ushort>.Count);
                }

                // Handle the last chunk in a vectorized way also.
                // We do a whole vector's worth again, but just mask out the bits we've already handled.
                if (remaining.Length > 0)
                {
                    Vector256<ushort> vector = Vector256.Create(sourceSpanUInt16.Slice(sourceSpanUInt16.Length - Vector256<ushort>.Count));
                    Vector256<byte> cmp = Vector256.Equals(vector, v1).AsByte() | Vector256.Equals(vector, v2).AsByte() | Vector256.Equals(vector, v3).AsByte();
                    int finalIndex = sourceSpanUInt16.Length - Vector256<ushort>.Count;
                    uint mask = cmp.ExtractMostSignificantBits() & 0x55555555 & ~((1u << (Vector256<byte>.Count - remaining.Length * sizeof(char))) - 1);
                    while (mask != 0)
                    {
                        uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
                        sepListBuilder.Append(finalIndex + (int)bitPos);
                        mask = BitOperations.ResetLowestSetBit(mask);
                    }
                }
                return;
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                Vector128<ushort> v1 = Vector128.Create((ushort)c);
                Vector128<ushort> v2 = Vector128.Create((ushort)c2);
                Vector128<ushort> v3 = Vector128.Create((ushort)c3);

                if (Sse2.IsSupported && PackedSpanHelpers.CanUsePackedIndexOf(c) && PackedSpanHelpers.CanUsePackedIndexOf(c2) && PackedSpanHelpers.CanUsePackedIndexOf(c3))
                {
                    // Process in double chunks & check if either chunk is likely to contain matches at once.
                    // If we get multiple matches in a single chunk, we assume they're likely to be close &
                    // break out of this logic & use the more optimistic loop below.
                    // This is similar logic to SpanHelpers.Packed.cs's IndexOf.
                    Vector128<byte> packedComparand1 = Vector128.Create((byte)c);
                    Vector128<byte> packedComparand2 = Vector128.Create((byte)c2);
                    Vector128<byte> packedComparand3 = Vector128.Create((byte)c3);
                    while ((uint)remaining.Length >= (uint)Vector128<ushort>.Count * 2)
                    {
                        Vector128<ushort> vector1 = Vector128.Create(remaining);
                        Vector128<ushort> vector2 = Vector128.Create(remaining.Slice(Vector128<ushort>.Count));
                        var packed = PackedSpanHelpers.PackSources(vector1.AsInt16(), vector2.AsInt16());

                        if ((Vector128.Equals(packed, packedComparand1) | Vector128.Equals(packed, packedComparand2) | Vector128.Equals(packed, packedComparand3)) != Vector128<byte>.Zero)
                        {
                            var cmp1 = Vector128.Equals(vector1, v1).AsByte() | Vector128.Equals(vector1, v2).AsByte() | Vector128.Equals(vector1, v3).AsByte();
                            var cmp2 = Vector128.Equals(vector2, v1).AsByte() | Vector128.Equals(vector2, v2).AsByte() | Vector128.Equals(vector2, v3).AsByte();

                            // Same logic as below, but for both vectors.
                            uint mask1 = cmp1.ExtractMostSignificantBits() & 0x5555;
                            uint mask2 = cmp2.ExtractMostSignificantBits() & 0x5555;
                            bool shouldBreak = uint.PopCount(mask1) + uint.PopCount(mask2) > 1;
                            while (mask1 != 0)
                            {
                                uint bitPos = (uint)BitOperations.TrailingZeroCount(mask1) / sizeof(char);
                                sepListBuilder.Append(baseIndex + (int)bitPos);
                                mask1 = BitOperations.ResetLowestSetBit(mask1);
                            }
                            while (mask2 != 0)
                            {
                                uint bitPos = (uint)BitOperations.TrailingZeroCount(mask2) / sizeof(char);
                                sepListBuilder.Append(baseIndex + (int)bitPos + Vector128<ushort>.Count);
                                mask2 = BitOperations.ResetLowestSetBit(mask2);
                            }

                            // Break out of the loop if we had > 1 match:
                            if (shouldBreak)
                            {
                                baseIndex += Vector128<ushort>.Count * 2;
                                remaining = remaining.Slice(Vector128<ushort>.Count * 2);
                                break;
                            }
                        }

                        baseIndex += Vector128<ushort>.Count * 2;
                        remaining = remaining.Slice(Vector128<ushort>.Count * 2);
                    }
                }

                while ((uint)remaining.Length >= (uint)Vector128<ushort>.Count)
                {
                    Vector128<ushort> vector = Vector128.Create(remaining);
                    Vector128<byte> cmp = Vector128.Equals(vector, v1).AsByte() | Vector128.Equals(vector, v2).AsByte() | Vector128.Equals(vector, v3).AsByte();

                    if (cmp != Vector128<byte>.Zero)
                    {
                        // Skip every other bit
                        uint mask = cmp.ExtractMostSignificantBits() & 0x5555;
                        do
                        {
                            uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
                            sepListBuilder.Append(baseIndex + (int)bitPos);
                            mask = BitOperations.ResetLowestSetBit(mask);
                        } while (mask != 0);
                    }

                    baseIndex += Vector128<ushort>.Count;
                    remaining = remaining.Slice(Vector128<ushort>.Count);
                }

                // Handle the last chunk in a vectorized way also.
                // We do a whole vector's worth again, but just mask out the bits we've already handled.
                if (remaining.Length > 0)
                {
                    Vector128<ushort> vector = Vector128.Create(sourceSpanUInt16.Slice(sourceSpanUInt16.Length - Vector128<ushort>.Count));
                    Vector128<byte> cmp = Vector128.Equals(vector, v1).AsByte() | Vector128.Equals(vector, v2).AsByte() | Vector128.Equals(vector, v3).AsByte();
                    int finalIndex = sourceSpanUInt16.Length - Vector128<ushort>.Count;
                    uint mask = cmp.ExtractMostSignificantBits() & 0x5555 & ~((1u << (Vector128<byte>.Count - remaining.Length * sizeof(char))) - 1);
                    while (mask != 0)
                    {
                        uint bitPos = (uint)BitOperations.TrailingZeroCount(mask) / sizeof(char);
                        sepListBuilder.Append(finalIndex + (int)bitPos);
                        mask = BitOperations.ResetLowestSetBit(mask);
                    }
                }
                return;
            }

            Debug.Fail("We should not be able to reach this point of MakeSeparatorListVectorized.");
        }

        /// <summary>
        /// Uses ValueListBuilder to create list that holds indexes of separators in string.
        /// </summary>
        /// <param name="source">The source to parse.</param>
        /// <param name="separator">separator string</param>
        /// <param name="sepListBuilder"><see cref="ValueListBuilder{T}"/> to store indexes</param>
        internal static void MakeSeparatorList(ReadOnlySpan<char> source, ReadOnlySpan<char> separator, ref ValueListBuilder<int> sepListBuilder)
        {
            Debug.Assert(!separator.IsEmpty, "Empty separator");

            int i = 0;
            while (!source.IsEmpty)
            {
                int index = source.IndexOf(separator);
                if (index < 0)
                {
                    break;
                }

                i += index;
                sepListBuilder.Append(i);

                i += separator.Length;
                source = source.Slice(index + separator.Length);
            }
        }

        /// <summary>
        /// Uses ValueListBuilder to create list that holds indexes of separators in string and list that holds length of separator strings.
        /// </summary>
        /// <param name="source">The source to parse.</param>
        /// <param name="separators">separator strngs</param>
        /// <param name="sepListBuilder"><see cref="ValueListBuilder{T}"/> for separator indexes</param>
        /// <param name="lengthListBuilder"><see cref="ValueListBuilder{T}"/> for separator length values</param>
        internal static void MakeSeparatorListAny(ReadOnlySpan<char> source, ReadOnlySpan<string?> separators, ref ValueListBuilder<int> sepListBuilder, ref ValueListBuilder<int> lengthListBuilder)
        {
            Debug.Assert(!separators.IsEmpty, "Zero separators");

            for (int i = 0; i < source.Length; i++)
            {
                for (int j = 0; j < separators.Length; j++)
                {
                    string? separator = separators[j];
                    if (IsNullOrEmpty(separator))
                    {
                        continue;
                    }
                    int currentSepLength = separator.Length;
                    if (source[i] == separator[0] && currentSepLength <= source.Length - i)
                    {
                        if (currentSepLength == 1 || source.Slice(i, currentSepLength).SequenceEqual(separator))
                        {
                            sepListBuilder.Append(i);
                            lengthListBuilder.Append(currentSepLength);
                            i += currentSepLength - 1;
                            break;
                        }
                    }
                }
            }
        }

        internal static void CheckStringSplitOptions(StringSplitOptions options)
        {
            const StringSplitOptions AllValidFlags = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

            if ((options & ~AllValidFlags) != 0)
            {
                // at least one invalid flag was set
                ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidFlag, ExceptionArgument.options);
            }
        }


        internal static int CompareOrdinal(ReadOnlySpan<char> strA, ReadOnlySpan<char> strB)
            => SpanHelpers.SequenceCompareTo(ref MemoryMarshal.GetReference(strA), strA.Length, ref MemoryMarshal.GetReference(strB), strB.Length);

        // From String.cs: NetWasm's string reaches its characters through
        // GetRawStringData, and one CoreLib serves both widths, so the copy keeps
        // upstream's width-neutral bounds check.
        internal bool TryGetSpan(int startIndex, int count, out ReadOnlySpan<char> slice)
        {
            if ((uint)startIndex > (uint)Length || (uint)count > (uint)(Length - startIndex))
            {
                slice = default;
                return false;
            }

            slice = new ReadOnlySpan<char>(ref Unsafe.Add(ref GetRawStringData(), (nint)(uint)startIndex /* force zero-extension */), count);
            return true;
        }

        internal static void CheckStringComparison(StringComparison comparisonType)
        {
            // Single comparison to check if comparisonType is within [CurrentCulture .. OrdinalIgnoreCase]
            if ((uint)comparisonType > (uint)StringComparison.OrdinalIgnoreCase)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.NotSupported_StringComparison, ExceptionArgument.comparisonType);
            }
        }
    }
}
