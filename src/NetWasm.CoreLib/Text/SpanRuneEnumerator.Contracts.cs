// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.

using System.Collections;
using System.Collections.Generic;

namespace System.Text;

public ref partial struct SpanRuneEnumerator : IEnumerator<Rune>, IEnumerator, IDisposable
{
    object IEnumerator.Current => _current;
    void IEnumerator.Reset() => throw new NotSupportedException();
    void IDisposable.Dispose() { }
}
