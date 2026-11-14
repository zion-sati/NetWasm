// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Upstream platform-not-supported implementation with invariant messages.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace System.Threading
{
    public sealed partial class ThreadPoolBoundHandle : IDisposable
    {
        public SafeHandle Handle => null!;

        private ThreadPoolBoundHandle()
        {
            Debug.Fail("Unreachable");
        }

        public static ThreadPoolBoundHandle BindHandle(SafeHandle handle)
        {
            ArgumentNullException.ThrowIfNull(handle);

            if (handle.IsClosed || handle.IsInvalid)
                throw new ArgumentException("The handle is invalid.", nameof(handle));

            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        [CLSCompliant(false)]
        public unsafe NativeOverlapped* AllocateNativeOverlapped(IOCompletionCallback callback, object? state, object? pinData)
        {
            ArgumentNullException.ThrowIfNull(callback);

            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        [CLSCompliant(false)]
        public unsafe NativeOverlapped* UnsafeAllocateNativeOverlapped(IOCompletionCallback callback, object? state, object? pinData) =>
            AllocateNativeOverlapped(callback, state, pinData);

        [CLSCompliant(false)]
        public unsafe NativeOverlapped* AllocateNativeOverlapped(PreAllocatedOverlapped preAllocated)
        {
            ArgumentNullException.ThrowIfNull(preAllocated);

            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        [CLSCompliant(false)]
        public unsafe void FreeNativeOverlapped(NativeOverlapped* overlapped)
        {
            ArgumentNullException.ThrowIfNull(overlapped);

            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        [CLSCompliant(false)]
        public static unsafe object? GetNativeOverlappedState(NativeOverlapped* overlapped)
        {
            ArgumentNullException.ThrowIfNull(overlapped);

            throw new PlatformNotSupportedException("This API is specific to the way in which Windows handles asynchronous I/O, and is not supported on this platform.");
        }

        public void Dispose()
        {
        }
    }
}
