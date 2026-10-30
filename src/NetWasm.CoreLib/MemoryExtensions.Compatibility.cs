// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.

namespace System;

public static partial class MemoryExtensions
{
    public static Text.SpanRuneEnumerator EnumerateRunes(this ReadOnlySpan<char> span) => new(span);

    public static Text.SpanRuneEnumerator EnumerateRunes(this Span<char> span) => new(span);
}
