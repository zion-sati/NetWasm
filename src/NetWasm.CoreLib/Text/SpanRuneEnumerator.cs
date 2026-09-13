// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public ref partial struct SpanRuneEnumerator
{
    private ReadOnlySpan<char> _remaining;
    private Rune _current;
    internal SpanRuneEnumerator(ReadOnlySpan<char> value) { _remaining = value; _current = default; }
    public Rune Current { get { return _current; } }
    public SpanRuneEnumerator GetEnumerator() => this;
    public bool MoveNext()
    {
        if (_remaining.IsEmpty) { _current = default; return false; }
        var status = Rune.DecodeFromUtf16(_remaining, out _current, out var consumed);
        if (status != System.Buffers.OperationStatus.Done) { _current = Rune.ReplacementChar; consumed = consumed == 0 ? 1 : consumed; }
        _remaining = _remaining.Slice(consumed); return true;
    }
}
