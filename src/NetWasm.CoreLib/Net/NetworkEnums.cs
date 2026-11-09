// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Data contracts from NETStandard.Library.Ref 2.1.0.

namespace System.Net;

[System.Flags]
public enum AuthenticationSchemes
{
    Anonymous = 32768,
    Basic = 8,
    Digest = 1,
    IntegratedWindowsAuthentication = 6,
    Negotiate = 2,
    None = 0,
    Ntlm = 4,
}

[System.Flags]
public enum DecompressionMethods
{
    Deflate = 2,
    GZip = 1,
    None = 0,
}

[System.Flags]
public enum SecurityProtocolType
{
    Ssl3 = 48,
    SystemDefault = 0,
    Tls = 192,
    Tls11 = 768,
    Tls12 = 3072,
}

public enum WebExceptionStatus
{
    CacheEntryNotFound = 18,
    ConnectFailure = 2,
    ConnectionClosed = 8,
    KeepAliveFailure = 12,
    MessageLengthLimitExceeded = 17,
    NameResolutionFailure = 1,
    Pending = 13,
    PipelineFailure = 5,
    ProtocolError = 7,
    ProxyNameResolutionFailure = 15,
    ReceiveFailure = 3,
    RequestCanceled = 6,
    RequestProhibitedByCachePolicy = 19,
    RequestProhibitedByProxy = 20,
    SecureChannelFailure = 10,
    SendFailure = 4,
    ServerProtocolViolation = 11,
    Success = 0,
    Timeout = 14,
    TrustFailure = 9,
    UnknownError = 16,
}
