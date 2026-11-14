// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

namespace System.Runtime.InteropServices;

[Serializable]
[System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public class ExternalException : SystemException
{
    private const int ErrorFail = unchecked((int)0x80004005);

    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    protected ExternalException(SerializationInfo info, StreamingContext context) : base(info, context) { }

    public ExternalException() : base("External component has thrown an exception.") =>
        HResult = ErrorFail;

    public ExternalException(string? message) : base(message) => HResult = ErrorFail;

    public ExternalException(string? message, Exception? innerException) :
        base(message, innerException) => HResult = ErrorFail;

    public ExternalException(string? message, int errorCode) : base(message) =>
        HResult = errorCode;

    public virtual int ErrorCode => HResult;

    // Upstream's ExternalException.ToString. This file is also compiled on its own by the desktop
    // NativeInteropSupport tests, so Environment.NewLine and a literal stand in for CoreLib's
    // internal NewLineConst and InnerExceptionPrefix (the same values).
    public override string ToString()
    {
        string message = Message;

        string s = $"{GetType()} (0x{HResult:X8})";

        if (!string.IsNullOrEmpty(message))
        {
            s += ": " + message;
        }

        Exception? innerException = InnerException;
        if (innerException != null)
        {
            s += Environment.NewLine + " ---> " + innerException.ToString();
        }

        if (StackTrace != null)
            s += Environment.NewLine + StackTrace;

        return s;
    }
}

[Serializable]
[System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
public class COMException : ExternalException
{
    private const int ErrorFail = unchecked((int)0x80004005);

    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    protected COMException(SerializationInfo info, StreamingContext context) : base(info, context) { }

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

    // Upstream's COMException.ToString, with the same public stand-ins as ExternalException.
    public override string ToString()
    {
        System.Text.StringBuilder s = new System.Text.StringBuilder();

        s.Append($"{GetType()} (0x{HResult:X8})");

        string message = Message;
        if (!string.IsNullOrEmpty(message))
        {
            s.Append(": ").Append(message);
        }

        Exception? innerException = InnerException;
        if (innerException != null)
        {
            s.Append(Environment.NewLine + " ---> ").Append(innerException.ToString());
        }

        string? stackTrace = StackTrace;
        if (stackTrace != null)
            s.AppendLine().Append(stackTrace);

        return s.ToString();
    }
}
