// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de src/libraries/System.Private.CoreLib/src/System/SearchValues/SearchValuesDebugView.cs

using System.Diagnostics;

namespace System.Buffers
{
    internal sealed class SearchValuesDebugView<T> where T : IEquatable<T>?
    {
        private readonly SearchValues<T> _values;

        public SearchValuesDebugView(SearchValues<T> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            _values = values;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] Values => _values.GetValues();
    }
}
