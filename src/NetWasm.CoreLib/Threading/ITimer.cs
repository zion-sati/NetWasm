// Portions derived from dotnet/runtime at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
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
