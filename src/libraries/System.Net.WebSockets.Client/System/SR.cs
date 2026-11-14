// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.WebSockets.Client uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.WebSockets.Client's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string net_WebSockets_InvalidCharInProtocolString = "The WebSocket protocol '{0}' is invalid because it contains the invalid character '{1}'.";
        internal const string net_WebSockets_InvalidCloseStatusCode = "The close status code '{0}' is reserved for system use only and cannot be specified when calling this method.";
        internal const string net_WebSockets_InvalidCloseStatusDescription = "The close status description '{0}' is too long. The UTF-8 representation of the status description must not be longer than {1} bytes.";
        internal const string net_WebSockets_ReasonNotNull = "The close status description '{0}' is invalid. When using close status code '{1}' the description must be null.";
        internal const string net_WebSockets_AlreadyStarted = "The WebSocket has already been started.";
        internal const string net_WebSockets_NotConnected = "The WebSocket is not connected.";
        internal const string net_WebSockets_Scheme = "Only Uris starting with 'ws://' or 'wss://' are supported.";
        internal const string net_uri_NotAbsolute = "This operation is not supported for a relative URI.";
        internal const string net_WebSockets_ArgumentOutOfRange_TooSmall = "The argument must be a value greater than {0}.";
        internal const string net_WebSockets_NoDuplicateProtocol = "Duplicate protocols are not allowed: '{0}'.";
        internal const string net_WebSockets_AcceptUnsupportedProtocol = "The WebSocket client request requested '{0}' protocol(s), but server is only accepting '{1}' protocol(s).";
        internal const string net_WebSockets_ClientWindowBitsNegotiationFailure = "The WebSocket failed to negotiate max client window bits. The client requested {0} but the server responded with {1}.";
        internal const string net_WebSockets_CompressionNotAvailable = "WebSocket per-message compression is not available on this NetWasm platform.";
        internal const string net_WebSockets_ConnectStatusExpected = "The server returned status code '{0}' when status code '{1}' was expected.";
        internal const string net_WebSockets_CustomInvokerRequiredForHttp2 = "An HttpMessageInvoker instance must be passed to ConnectAsync when using HTTP/2.";
        internal const string net_WebSockets_InvalidResponseHeader = "The '{0}' header value '{1}' is invalid.";
        internal const string net_WebSockets_MissingResponseHeader = "The server's response was missing the required header '{0}'.";
        internal const string net_WebSockets_ServerWindowBitsNegotiationFailure = "The WebSocket failed to negotiate max server window bits. The client requested {0} but the server responded with {1}.";
        internal const string net_webstatus_ConnectFailure = "Unable to connect to the remote server";
    }
}
