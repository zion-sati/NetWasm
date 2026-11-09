// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm has no native socket errno mapping yet. Explicit SocketError values
// are preserved; messages describe that managed value without a host lookup.
using System.ComponentModel;
using System.Runtime.Serialization;

namespace System.Net.Sockets;

[Serializable]
public class SocketException : Win32Exception
{
    private readonly SocketError _errorCode;

    public SocketException() : base(0, null) => throw new PlatformNotSupportedException("SocketException host last-error lookup is not available on this NetWasm platform.");
    public SocketException(int errorCode) : this((SocketError)errorCode) { }
    public SocketException(int errorCode, string? message) : this((SocketError)errorCode, message) { }
    internal SocketException(SocketError errorCode) : this(errorCode, $"Socket error: {errorCode}.") { }
    internal SocketException(SocketError errorCode, string? message) : base((int)errorCode, message) { _errorCode = errorCode; }
    protected SocketException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    public override string Message => base.Message;
    public SocketError SocketErrorCode => _errorCode;
    public override int ErrorCode => NativeErrorCode;
}
