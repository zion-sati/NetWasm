// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System;

public static partial class MemoryExtensions
{
    public static Text.SpanRuneEnumerator EnumerateRunes(this ReadOnlySpan<char> span) => new(span);

    public static Text.SpanRuneEnumerator EnumerateRunes(this Span<char> span) => new(span);
}
