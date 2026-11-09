// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// Borrowed/invalid handles remain managed wrappers. Valid host ownership is deferred.
namespace Microsoft.Win32.SafeHandles;

public sealed class SafeWaitHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeWaitHandle() : base(true) { }
    public SafeWaitHandle(System.IntPtr existingHandle, bool ownsHandle) : base(ownsHandle)
    {
        if (ownsHandle && existingHandle != System.IntPtr.Zero && existingHandle != new System.IntPtr(-1))
            throw new System.PlatformNotSupportedException("SafeWaitHandle ownership requires host handle release, which is not available on this NetWasm platform.");
        SetHandle(existingHandle);
    }
    protected override bool ReleaseHandle() =>
        throw new System.PlatformNotSupportedException("SafeWaitHandle host release is not available on this NetWasm platform.");
}
