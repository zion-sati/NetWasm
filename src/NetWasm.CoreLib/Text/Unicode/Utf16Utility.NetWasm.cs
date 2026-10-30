// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's 64-bit forms of upstream Utf16Utility's ASCII case tests, which String.cs's ordinal
// ignore-case comparison uses until upstream's String partials replace it.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Text.Unicode
{
    internal static partial class Utf16Utility
    {
        // The UInt32 helpers' test, over four ASCII characters.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UInt64ContainsAnyLowercaseAsciiChar(ulong value)
        {
            Debug.Assert(AllCharsInUInt64AreAscii(value));

            // the 0x80 bit of each word is set iff the word has value >= 'a' and <= 'z'
            ulong lowerIndicator = value + 0x0080_0080_0080_0080ul - 0x0061_0061_0061_0061ul;
            ulong upperIndicator = value + 0x0080_0080_0080_0080ul - 0x007B_007B_007B_007Bul;
            return ((lowerIndicator ^ upperIndicator) & 0x0080_0080_0080_0080ul) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool UInt64ContainsAnyUppercaseAsciiChar(ulong value)
        {
            Debug.Assert(AllCharsInUInt64AreAscii(value));

            // the 0x80 bit of each word is set iff the word has value >= 'A' and <= 'Z'
            ulong lowerIndicator = value + 0x0080_0080_0080_0080ul - 0x0041_0041_0041_0041ul;
            ulong upperIndicator = value + 0x0080_0080_0080_0080ul - 0x005B_005B_005B_005Bul;
            return ((lowerIndicator ^ upperIndicator) & 0x0080_0080_0080_0080ul) != 0;
        }
    }
}
