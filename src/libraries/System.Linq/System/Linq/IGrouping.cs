// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// From dotnet/runtime 474ed96's System.Linq Grouping.cs. NetWasm declares this interface in CoreLib, as
// Queryable and ParallelEnumerable here use it; NetWasm.System.Linq forwards it here.

using System.Collections.Generic;

namespace System.Linq
{
    public interface IGrouping<out TKey, out TElement> : IEnumerable<TElement>
    {
        TKey Key { get; }
    }
}
