// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm: upstream's message constructors reach Win32Exception(string) and (string, Exception),
// which capture the last P/Invoke error; NetWasm keeps no saved last-error state, so they pass
// native code 0 explicitly through Win32Exception's (int, string) and internal
// (int, string, Exception) constructors. NativeErrorCode and ErrorCode are therefore 0 where
// desktop reports an ambient last error; WebSocketErrorCode, messages, inner exceptions and
// the explicit nativeError HResult assignments match upstream. The parameterless and int-only
// constructors keep upstream's chain, which reaches Win32Exception's unsupported last-error
// and message lookups and throws PlatformNotSupportedException.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace System.Net.WebSockets
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed class WebSocketException : Win32Exception
    {
        // No native operation supplied an error for managed protocol and message exceptions.
        private const int NoNativeError = 0;

        private readonly WebSocketError _webSocketErrorCode;

        public WebSocketException()
            : this(Marshal.GetLastPInvokeError())
        {
        }

        public WebSocketException(WebSocketError error)
            : this(error, GetErrorMessage(error))
        {
        }

        public WebSocketException(WebSocketError error, string? message) : base(NoNativeError, message ?? SR.net_WebSockets_Generic)
        {
            _webSocketErrorCode = error;
        }

        public WebSocketException(WebSocketError error, Exception? innerException)
            : this(error, GetErrorMessage(error), innerException)
        {
        }

        public WebSocketException(WebSocketError error, string? message, Exception? innerException)
            : base(NoNativeError, message ?? SR.net_WebSockets_Generic, innerException)
        {
            _webSocketErrorCode = error;
        }

        public WebSocketException(int nativeError)
            : base(nativeError)
        {
            _webSocketErrorCode = !Succeeded(nativeError) ? WebSocketError.NativeError : WebSocketError.Success;
            SetErrorCodeOnError(nativeError);
        }

        public WebSocketException(int nativeError, string? message)
            : base(nativeError, message)
        {
            _webSocketErrorCode = !Succeeded(nativeError) ? WebSocketError.NativeError : WebSocketError.Success;
            SetErrorCodeOnError(nativeError);
        }

        public WebSocketException(int nativeError, Exception? innerException)
            : base(NoNativeError, SR.net_WebSockets_Generic, innerException)
        {
            _webSocketErrorCode = !Succeeded(nativeError) ? WebSocketError.NativeError : WebSocketError.Success;
            SetErrorCodeOnError(nativeError);
        }

        public WebSocketException(WebSocketError error, int nativeError)
            : this(error, nativeError, GetErrorMessage(error))
        {
        }

        public WebSocketException(WebSocketError error, int nativeError, string? message)
            : base(NoNativeError, message ?? SR.net_WebSockets_Generic)
        {
            _webSocketErrorCode = error;
            SetErrorCodeOnError(nativeError);
        }

        public WebSocketException(WebSocketError error, int nativeError, Exception? innerException)
            : this(error, nativeError, GetErrorMessage(error), innerException)
        {
        }

        public WebSocketException(WebSocketError error, int nativeError, string? message, Exception? innerException)
            : base(NoNativeError, message ?? SR.net_WebSockets_Generic, innerException)
        {
            _webSocketErrorCode = error;
            SetErrorCodeOnError(nativeError);
        }

        public WebSocketException(string? message)
            : base(NoNativeError, message ?? SR.net_WebSockets_Generic)
        {
        }

        public WebSocketException(string? message, Exception? innerException)
            : base(NoNativeError, message ?? SR.net_WebSockets_Generic, innerException)
        {
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        private WebSocketException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(WebSocketErrorCode), _webSocketErrorCode);
        }

        public override int ErrorCode
        {
            get
            {
                return base.NativeErrorCode;
            }
        }

        public WebSocketError WebSocketErrorCode
        {
            get
            {
                return _webSocketErrorCode;
            }
        }

        private static string GetErrorMessage(WebSocketError error) =>
            // Provide a canned message for the error type.
            error switch
            {
                WebSocketError.InvalidMessageType => SR.Format(SR.net_WebSockets_InvalidMessageType_Generic,
                       $"{nameof(WebSocket)}.{nameof(WebSocket.CloseAsync)}",
                       $"{nameof(WebSocket)}.{nameof(WebSocket.CloseOutputAsync)}"),
                WebSocketError.Faulted => SR.net_Websockets_WebSocketBaseFaulted,
                WebSocketError.NotAWebSocket => SR.net_WebSockets_NotAWebSocket_Generic,
                WebSocketError.UnsupportedVersion => SR.net_WebSockets_UnsupportedWebSocketVersion_Generic,
                WebSocketError.UnsupportedProtocol => SR.net_WebSockets_UnsupportedProtocol_Generic,
                WebSocketError.HeaderError => SR.net_WebSockets_HeaderError_Generic,
                WebSocketError.ConnectionClosedPrematurely => SR.net_WebSockets_ConnectionClosedPrematurely_Generic,
                WebSocketError.InvalidState => SR.net_WebSockets_InvalidState_Generic,
                _ => SR.net_WebSockets_Generic,
            };

        // Set the error code only if there is an error (i.e. nativeError >= 0). Otherwise the code fails during deserialization
        // as the Exception..ctor() throws on setting HResult to 0. The default for HResult is -2147467259.
        private void SetErrorCodeOnError(int nativeError)
        {
            if (!Succeeded(nativeError))
            {
                HResult = nativeError;
            }
        }

        private static bool Succeeded(int hr)
        {
            return (hr >= 0);
        }
    }
}
