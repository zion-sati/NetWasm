// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections;
using System.Collections.Generic;

namespace System.Text;

public readonly partial struct RunePosition
{
    public static Utf8Enumerator EnumerateUtf8(ReadOnlySpan<byte> span) => new(span);

    public void Deconstruct(out Rune rune, out int startIndex)
    {
        rune = Rune;
        startIndex = StartIndex;
    }

    public void Deconstruct(out Rune rune, out int startIndex, out int length)
    {
        rune = Rune;
        startIndex = StartIndex;
        length = Length;
    }

    public ref struct Utf8Enumerator : IEnumerator<RunePosition>, IEnumerator, IDisposable
    {
        private readonly ReadOnlySpan<byte> _original;
        private ReadOnlySpan<byte> _remaining;
        private RunePosition _current;

        internal Utf8Enumerator(ReadOnlySpan<byte> buffer)
        {
            _original = buffer;
            _remaining = buffer;
            _current = default;
        }

        public RunePosition Current { get { return _current; } }
        public Utf8Enumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                _current = default;
                return false;
            }

            var status = Rune.DecodeFromUtf8(_remaining, out var rune, out var consumed);
            var replaced = status != Buffers.OperationStatus.Done;
            if (replaced)
            {
                rune = Rune.ReplacementChar;
                consumed = consumed == 0 ? 1 : consumed;
            }

            _current = new RunePosition(rune, _current.StartIndex + _current.Length, consumed, replaced);
            _remaining = _remaining.Slice(consumed);
            return true;
        }

        public void Reset()
        {
            _remaining = _original;
            _current = default;
        }

        object IEnumerator.Current => _current;
        void IEnumerator.Reset() => Reset();
        void IDisposable.Dispose() { }
    }
}

// Keep the existing UTF-16 pattern enumerator usable through the upstream
// IEnumerator contract without changing its core decoding implementation.
public readonly partial struct RunePosition
{
    public ref partial struct Utf16Enumerator : IEnumerator<RunePosition>, IEnumerator, IDisposable
    {
        object IEnumerator.Current => _current;
        void IEnumerator.Reset() => Reset();
        void IDisposable.Dispose() { }
    }
}
