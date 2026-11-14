// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Net.WebSockets
{
    internal static class SR
    {
        internal const string NotReadableStream =
            "The base stream is not readable.";
        internal const string NotWriteableStream =
            "The base stream is not writeable.";
        internal const string net_WebSockets_AcceptUnsupportedProtocol =
            "The WebSocket client request requested '{0}' protocol(s), but server is only accepting '{1}' protocol(s).";
        internal const string net_WebSockets_AlreadyStarted =
            "The WebSocket has already been started.";
        internal const string net_WebSockets_ArgumentOutOfRange =
            "The argument must be a value between {0} and {1}.";
        internal const string net_WebSockets_ArgumentOutOfRange_TooSmall =
            "The argument must be a value greater than {0}.";
        internal const string net_WebSockets_Argument_InvalidMessageType =
            "The message type '{0}' is not allowed for the '{1}' operation. Valid message types are: '{2}, {3}'. To close the WebSocket, use the '{4}' operation instead. ";
        internal const string net_WebSockets_Argument_MessageFlagsHasDifferentCompressionOptions =
            "The compression options for a continuation cannot be different than the options used to send the first fragment of the message.";
        internal const string net_WebSockets_ClientWindowBitsNegotiationFailure =
            "The WebSocket failed to negotiate max client window bits. The client requested {0} but the server responded with {1}.";
        internal const string net_WebSockets_CompressionNotAvailable =
            "WebSocket per-message compression is not available on this NetWasm platform.";
        internal const string net_WebSockets_ConnectNotSupported =
            "ClientWebSocket connections are not available on this NetWasm platform: there is no host transport to upgrade. Use WebSocket.CreateFromStream with a connected stream instead.";
        internal const string net_WebSockets_ConnectStatusExpected =
            "The server returned status code '{0}' when status code '{1}' was expected.";
        internal const string net_WebSockets_ConnectionClosedPrematurely_Generic =
            "The remote party closed the WebSocket connection without completing the close handshake.";
        internal const string net_WebSockets_CustomInvokerRequiredForHttp2 =
            "An HttpMessageInvoker instance must be passed to ConnectAsync when using HTTP/2.";
        internal const string net_WebSockets_Generic =
            "An internal WebSocket error occurred. Please see the innerException, if present, for more details. ";
        internal const string net_WebSockets_HeaderError_Generic =
            "The WebSocket request or response contained unsupported header(s). ";
        internal const string net_WebSockets_InvalidCharInProtocolString =
            "The WebSocket protocol '{0}' is invalid because it contains the invalid character '{1}'.";
        internal const string net_WebSockets_InvalidCloseStatusCode =
            "The close status code '{0}' is reserved for system use only and cannot be specified when calling this method.";
        internal const string net_WebSockets_InvalidCloseStatusDescription =
            "The close status description '{0}' is too long. The UTF-8 representation of the status description must not be longer than {1} bytes.";
        internal const string net_WebSockets_InvalidMessageType_Generic =
            "The received  message type is invalid after calling {0}. {0} should only be used if no more data is expected from the remote endpoint. Use '{1}' instead to keep being able to receive data but close the output channel.";
        internal const string net_WebSockets_InvalidResponseHeader =
            "The '{0}' header value '{1}' is invalid.";
        internal const string net_WebSockets_InvalidState =
            "The WebSocket is in an invalid state ('{0}') for this operation. Valid states are: '{1}'";
        internal const string net_WebSockets_InvalidState_Generic =
            "The WebSocket instance cannot be used for communication because it has been transitioned into an invalid state.";
        internal const string net_WebSockets_MissingResponseHeader =
            "The server's response was missing the required header '{0}'.";
        internal const string net_WebSockets_NoDuplicateProtocol =
            "Duplicate protocols are not allowed: '{0}'.";
        internal const string net_WebSockets_NotAWebSocket_Generic =
            "A WebSocket operation was called on a request or response that is not a WebSocket.";
        internal const string net_WebSockets_NotConnected =
            "The WebSocket is not connected.";
        internal const string net_WebSockets_ReasonNotNull =
            "The close status description '{0}' is invalid. When using close status code '{1}' the description must be null.";
        internal const string net_WebSockets_Scheme =
            "Only Uris starting with 'ws://' or 'wss://' are supported.";
        internal const string net_WebSockets_ServerWindowBitsNegotiationFailure =
            "The WebSocket failed to negotiate max server window bits. The client requested {0} but the server responded with {1}.";
        internal const string net_WebSockets_TimeoutOutOfRange =
            "The timeout must be a value between non-negative or Timeout.InfiniteTimeSpan.";
        internal const string net_WebSockets_UnsupportedProtocol_Generic =
            "The WebSocket request or response operation was called with unsupported protocol(s). ";
        internal const string net_WebSockets_UnsupportedWebSocketVersion_Generic =
            "Unsupported WebSocket version.";
        internal const string net_Websockets_AlreadyOneOutstandingOperation =
            "There is already one outstanding '{0}' call for this WebSocket instance. ReceiveAsync and SendAsync can be called simultaneously, but at most one outstanding operation for each of them is allowed at the same time.";
        internal const string net_Websockets_ClientReceivedMaskedFrame =
            "The WebSocket server sent a masked frame.";
        internal const string net_Websockets_ContinuationFromFinalFrame =
            "The WebSocket received a continuation frame from a previous final message.";
        internal const string net_Websockets_InvalidCloseDescriptionPayload =
            "The WebSocket received a close frame whose status description was not valid UTF-8.";
        internal const string net_Websockets_InvalidCloseStatusCodeReceived =
            "The WebSocket received a close frame containing a reserved or invalid close status code.";
        internal const string net_Websockets_InvalidControlMessage =
            "The WebSocket received an invalid control message.";
        internal const string net_Websockets_InvalidPayloadLength =
            "The WebSocket received a frame with an invalid payload length.";
        internal const string net_Websockets_InvalidTextPayload =
            "The WebSocket received a text message whose payload was not valid UTF-8.";
        internal const string net_Websockets_KeepAlivePingTimeout =
            "The WebSocket didn't recieve a Pong frame in response to a Ping frame within the configured KeepAliveTimeout.";
        internal const string net_Websockets_NonContinuationAfterNonFinalFrame =
            "The WebSocket expected a continuation frame after having received a previous non-final frame.";
        internal const string net_Websockets_PerMessageCompressedFlagInContinuation =
            "The WebSocket received a continuation frame with Per-Message Compressed flag set.";
        internal const string net_Websockets_PerMessageCompressedFlagWhenNotEnabled =
            "The WebSocket received compressed frame when compression is not enabled.";
        internal const string net_Websockets_ProtocolViolation =
            "The WebSocket peer sent invalid or malformed data, violating the WebSocket protocol.";
        internal const string net_Websockets_ReservedBitsSet =
            "The WebSocket received a frame with one or more reserved bits set.";
        internal const string net_Websockets_ServerReceivedUnmaskedFrame =
            "The WebSocket client sent an unmasked frame.";
        internal const string net_Websockets_UnknownOpcode =
            "The WebSocket received a frame with an unknown opcode: '0x{0}'.";
        internal const string net_Websockets_WebSocketBaseFaulted =
            "An exception caused the WebSocket to enter the Aborted state. Please see the InnerException, if present, for more details.";
        internal const string net_uri_NotAbsolute =
            "This operation is not supported for a relative URI.";
        internal const string net_webstatus_ConnectFailure =
            "Unable to connect to the remote server";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);

        internal static string Format(string format, params object?[] args) =>
            string.Format(format, args);
    }
}
