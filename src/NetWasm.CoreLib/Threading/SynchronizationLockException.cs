// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib SynchronizationLockException.

namespace System.Threading;

[Serializable]
[System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public class SynchronizationLockException : SystemException
{
    // Retain the standard constructor for derived-library compatibility.
    [System.Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    protected SynchronizationLockException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
        throw new System.PlatformNotSupportedException("SynchronizationLockException formatter deserialization is not available on this NetWasm platform.");

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
