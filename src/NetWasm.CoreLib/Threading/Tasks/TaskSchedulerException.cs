// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.Tasks;

public class TaskSchedulerException : Exception
{
    private const string DefaultMessage = "An exception was thrown by a TaskScheduler.";

    public TaskSchedulerException() : base(DefaultMessage) { }
    public TaskSchedulerException(string? message) : base(message ?? DefaultMessage) { }
    public TaskSchedulerException(Exception? innerException)
        : base(DefaultMessage, innerException) { }
    public TaskSchedulerException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException) { }
}
