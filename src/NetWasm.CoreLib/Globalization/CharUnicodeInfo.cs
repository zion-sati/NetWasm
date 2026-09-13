// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization;

public static partial class CharUnicodeInfo
{
    public static UnicodeCategory GetUnicodeCategory(char value) =>
        (UnicodeCategory)(CategoriesValues[GetCategoryCasingOffset(value)] & 0x1f);

    internal static char ToUpper(char value) => ApplyCasing(value, UppercaseValues);

    internal static char ToLower(char value) => ApplyCasing(value, LowercaseValues);

    private static char ApplyCasing(char value, ReadOnlySpan<byte> deltas)
    {
        int byteOffset = GetCategoryCasingOffset(value) * 2;
        short delta = (short)(deltas[byteOffset] | deltas[byteOffset + 1] << 8);
        return (char)(value + delta);
    }

    private static int GetCategoryCasingOffset(uint codePoint)
    {
        uint index = CategoryCasingLevel1Index[(int)(codePoint >> 9)];
        int level2Offset = (int)((index << 6) + ((codePoint >> 3) & 0x3e));
        ReadOnlySpan<byte> level2 = CategoryCasingLevel2Index;
        index = (uint)(level2[level2Offset] | level2[level2Offset + 1] << 8);
        return CategoryCasingLevel3Index[(int)((index << 4) + (codePoint & 0x0f))];
    }
}
