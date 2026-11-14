// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.Tasks;

[Serializable]
[System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public class TaskSchedulerException : Exception
{
    // Retain the standard constructor for derived-library compatibility.
    [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
    protected TaskSchedulerException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
        throw new System.PlatformNotSupportedException("TaskSchedulerException formatter deserialization is not available on this NetWasm platform.");

    private const string DefaultMessage = "An exception was thrown by a TaskScheduler.";

    public TaskSchedulerException() : base(DefaultMessage) { }
    public TaskSchedulerException(string? message) : base(message ?? DefaultMessage) { }
    public TaskSchedulerException(Exception? innerException)
        : base(DefaultMessage, innerException) { }
    public TaskSchedulerException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException) { }
}
