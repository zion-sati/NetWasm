// Portions derived from dotnet/runtime at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Threading
{
    public delegate void TimerCallback(object? state);

    public interface ITimer : IDisposable, IAsyncDisposable
    {
        bool Change(TimeSpan dueTime, TimeSpan period);
    }
}
