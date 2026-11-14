// Ported from dotnet/runtime System.Private.CoreLib; upstream commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
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
