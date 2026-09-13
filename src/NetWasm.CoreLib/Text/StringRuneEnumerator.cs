// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
using System.Collections;
using System.Collections.Generic;

namespace System.Text;

public struct StringRuneEnumerator : IEnumerable<Rune>, IEnumerator<Rune>, IEnumerable, IEnumerator, IDisposable
{
    private readonly string _value;
    private Rune _current;
    private int _index;

    internal StringRuneEnumerator(string value) { _value = value; _current = default; _index = 0; }
    public Rune Current { get { return _current; } }
    object IEnumerator.Current => _current;
    public StringRuneEnumerator GetEnumerator() => this;
    IEnumerator<Rune> IEnumerable<Rune>.GetEnumerator() => this;
    IEnumerator IEnumerable.GetEnumerator() => this;
    public bool MoveNext()
    {
        if ((uint)_index >= (uint)_value.Length) { _current = default; return false; }
        if (!Rune.TryGetRuneAt(_value, _index, out _current)) _current = Rune.ReplacementChar;
        _index += _current.Utf16SequenceLength;
        return true;
    }
    public void Reset() { _index = 0; _current = default; }
    public void Dispose() { }
}
