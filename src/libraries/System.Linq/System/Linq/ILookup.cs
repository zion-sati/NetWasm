// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime 474ed96's System.Linq Lookup.cs. NetWasm declares this interface in CoreLib, as
// Queryable and ParallelEnumerable here use it; NetWasm.System.Linq forwards it here.

using System.Collections.Generic;

namespace System.Linq
{
    public interface ILookup<TKey, TElement> : IEnumerable<IGrouping<TKey, TElement>>
    {
        int Count { get; }

        IEnumerable<TElement> this[TKey key] { get; }

        bool Contains(TKey key);
    }
}
