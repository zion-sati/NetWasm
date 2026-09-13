// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public readonly partial struct RunePosition : IEquatable<RunePosition>
{
    public RunePosition(Rune rune, int startIndex, int length, bool wasReplaced) { Rune = rune; StartIndex = startIndex; Length = length; WasReplaced = wasReplaced; }
    public Rune Rune { get; }
    public int StartIndex { get; }
    public int Length { get; }
    public bool WasReplaced { get; }
    public static Utf16Enumerator EnumerateUtf16(ReadOnlySpan<char> span) => new(span);
    public bool Equals(RunePosition other) => Rune == other.Rune && StartIndex == other.StartIndex && Length == other.Length && WasReplaced == other.WasReplaced;
    public override bool Equals(object? value) => value is RunePosition other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Rune, StartIndex, Length, WasReplaced);
    public static bool operator ==(RunePosition left, RunePosition right) => left.Equals(right);
    public static bool operator !=(RunePosition left, RunePosition right) => !left.Equals(right);

    public ref partial struct Utf16Enumerator
    {
        private ReadOnlySpan<char> _value;
        private int _index;
        private RunePosition _current;
        internal Utf16Enumerator(ReadOnlySpan<char> value) { _value = value; _index = 0; _current = default; }
        public RunePosition Current { get { return _current; } }
        public Utf16Enumerator GetEnumerator() => this;
        public bool MoveNext()
        {
            if ((uint)_index >= (uint)_value.Length) return false;
            var start = _index; var status = Rune.DecodeFromUtf16(_value.Slice(_index), out var rune, out var consumed); var replaced = status != System.Buffers.OperationStatus.Done; if (replaced) { rune = Rune.ReplacementChar; consumed = consumed == 0 ? 1 : consumed; }
            _index += consumed; _current = new RunePosition(rune, start, consumed, replaced); return true;
        }
        public void Reset() { _index = 0; _current = default; }
    }
}
