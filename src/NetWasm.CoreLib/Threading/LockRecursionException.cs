// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib LockRecursionException.

namespace System.Threading;

public class LockRecursionException : Exception
{
    public LockRecursionException()
    {
    }

    public LockRecursionException(string? message)
        : base(message)
    {
    }

    public LockRecursionException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
