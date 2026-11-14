// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Upstream platform-not-supported implementation with invariant messages.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading
{
    public sealed class PreAllocatedOverlapped : IDisposable
    {
        [CLSCompliant(false)]
        public PreAllocatedOverlapped(IOCompletionCallback callback, object? state, object? pinData)
        {
            ArgumentNullException.ThrowIfNull(callback);
            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        [CLSCompliant(false)]
        public static PreAllocatedOverlapped UnsafeCreate(IOCompletionCallback callback, object? state, object? pinData)
        {
            ArgumentNullException.ThrowIfNull(callback);
            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        public void Dispose() { }
    }
}
