// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.InteropServices;

public class ExternalException : SystemException
{
    private const int ErrorFail = unchecked((int)0x80004005);

    public ExternalException() : base("External component has thrown an exception.") =>
        HResult = ErrorFail;

    public ExternalException(string? message) : base(message) => HResult = ErrorFail;

    public ExternalException(string? message, Exception? innerException) :
        base(message, innerException) => HResult = ErrorFail;

    public ExternalException(string? message, int errorCode) : base(message) =>
        HResult = errorCode;
}

public class COMException : ExternalException
{
    private const int ErrorFail = unchecked((int)0x80004005);

    public COMException() : base("Error in the application.", ErrorFail)
    {
    }

    public COMException(string? message) : base(message, ErrorFail)
    {
    }

    public COMException(string? message, Exception? innerException) :
        base(message, innerException)
    {
        HResult = ErrorFail;
    }

    public COMException(string? message, int errorCode) : base(message, errorCode)
    {
    }
}
