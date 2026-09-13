// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text.Unicode;

public static class Utf16
{
    public static bool IsValid(ReadOnlySpan<char> value) => IndexOfInvalidSubsequence(value) < 0;
    public static int IndexOfInvalidSubsequence(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++) { var current = value[index]; if (current >= 0xD800 && current <= 0xDBFF) { if (index + 1 >= value.Length || value[index + 1] < 0xDC00 || value[index + 1] > 0xDFFF) return index; index++; } else if (current >= 0xDC00 && current <= 0xDFFF) return index; }
        return -1;
    }
}
