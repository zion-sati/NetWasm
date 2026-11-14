// Ported from dotnet/runtime System.Private.CoreLib; upstream commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections;
using System.Collections.Generic;

namespace System.Text;

public ref partial struct SpanRuneEnumerator : IEnumerator<Rune>, IEnumerator, IDisposable
{
    object IEnumerator.Current => _current;
    void IEnumerator.Reset() => throw new NotSupportedException();
    void IDisposable.Dispose() { }
}
