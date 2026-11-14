// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce src/libraries/System.Private.CoreLib/src/System/SpanDebugView.cs

using System.Diagnostics;

namespace System
{
    internal sealed class SpanDebugView<T>
    {
        private readonly T[] _array;

        public SpanDebugView(Span<T> span)
        {
            _array = span.ToArray();
        }

        public SpanDebugView(ReadOnlySpan<T> span)
        {
            _array = span.ToArray();
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Items => _array;
    }
}
