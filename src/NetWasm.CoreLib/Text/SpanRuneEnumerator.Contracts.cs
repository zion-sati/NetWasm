// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections;
using System.Collections.Generic;

namespace System.Text;

public ref partial struct SpanRuneEnumerator : IEnumerator<Rune>, IEnumerator, IDisposable
{
    object IEnumerator.Current => _current;
    void IEnumerator.Reset() => throw new NotSupportedException();
    void IDisposable.Dispose() { }
}
