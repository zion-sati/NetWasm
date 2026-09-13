// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace System
{
    public interface IAsyncDisposable
    {
        ValueTask DisposeAsync();
    }
}

namespace System.Collections.Generic
{
    public interface IAsyncEnumerable<out T>
        where T : allows ref struct
    {
        IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default(System.Threading.CancellationToken));
    }

    public interface IAsyncEnumerator<out T> : IAsyncDisposable
        where T : allows ref struct
    {
        ValueTask<bool> MoveNextAsync();
        T Current { get; }
    }
}
