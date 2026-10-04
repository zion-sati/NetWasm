// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib SynchronizationLockException.

namespace System.Threading;

public class SynchronizationLockException : SystemException
{
    public SynchronizationLockException()
        : base("Object synchronization method was called from an unsynchronized block of code.")
    {
    }

    public SynchronizationLockException(string? message)
        : base(message)
    {
    }

    public SynchronizationLockException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
