// Portions derived from dotnet/runtime System.Private.CoreLib StringBuilderRuneEnumerator.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;

namespace System.Text;

public struct StringBuilderRuneEnumerator : IEnumerable<Rune>, IEnumerator<Rune>, IEnumerable, IEnumerator, IDisposable
{
    private readonly StringBuilder _stringBuilder;
    private Rune _current;
    private int _nextIndex;

    internal StringBuilderRuneEnumerator(StringBuilder value)
    {
        _stringBuilder = value;
        _current = default;
        _nextIndex = 0;
    }

    public Rune Current
    {
        get { return _current; }
    }
    object IEnumerator.Current => _current;
    public StringBuilderRuneEnumerator GetEnumerator() => this;
    IEnumerator<Rune> IEnumerable<Rune>.GetEnumerator() => this;
    IEnumerator IEnumerable.GetEnumerator() => this;

    public bool MoveNext()
    {
        if ((uint)_nextIndex >= (uint)_stringBuilder.Length)
        {
            _current = default;
            return false;
        }

        if (!_stringBuilder.TryGetRuneAt(_nextIndex, out _current))
        {
            _current = Rune.ReplacementChar;
        }

        _nextIndex += _current.Utf16SequenceLength;
        return true;
    }

    void IEnumerator.Reset()
    {
        _current = default;
        _nextIndex = 0;
    }

    void IDisposable.Dispose()
    {
    }
}
