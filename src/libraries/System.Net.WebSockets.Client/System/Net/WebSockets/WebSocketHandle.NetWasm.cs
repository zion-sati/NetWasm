// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (System.Net.WebSockets.Client WebSocketHandle.Managed.cs).
// NetWasm adaptation: upstream sends the upgrade request through an HttpMessageInvoker over
// SocketsHttpHandler and wraps the response's connection stream. NetWasm's HttpClient runs over
// wasi:http, which cannot upgrade a connection, so the request and response (UpgradeRequest and
// UpgradeResponse at the end of this file) are an HTTP/1.1 exchange over a TCP Socket that does what
// SocketsHttpHandler does for it, including the cookie container; the rest is upstream's. Without an
// invoker, HTTP/2 is rejected as upstream rejects it, and what the exchange cannot do throws
// PlatformNotSupportedException before connecting: wss:// (no TLS), proxies, HTTP authentication
// and permessage-deflate (no zlib). The response status code is not exposed (HttpStatusCode lives
// with HttpClient, outside CoreLib).

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.WebSockets
{
    internal sealed class WebSocketHandle
    {
        private readonly CancellationTokenSource _abortSource = new CancellationTokenSource();
        private WebSocketState _state = WebSocketState.Connecting;
        private WebSocketDeflateOptions? _negotiatedDeflateOptions;

        public WebSocket? WebSocket { get; private set; }
        public WebSocketState State => WebSocket?.State ?? _state;

        public IReadOnlyDictionary<string, IEnumerable<string>>? HttpResponseHeaders { get; set; }

        public static ClientWebSocketOptions CreateDefaultOptions() => new ClientWebSocketOptions() { Proxy = DefaultWebProxy.Instance };

        public void Dispose()
        {
            _state = WebSocketState.Closed;
            WebSocket?.Dispose();
        }

        public void Abort()
        {
            _abortSource.Cancel();
            WebSocket?.Abort();
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken, ClientWebSocketOptions options)
        {
            if (options.HttpVersion.Major >= 2)
            {
                throw new ArgumentException(SR.net_WebSockets_CustomInvokerRequiredForHttp2, nameof(options));
            }

            ThrowIfUnsupported(uri, options);

            UpgradeResponse? response = null;
            bool disposeResponse = false;
            try
            {
                var request = new UpgradeRequest(uri);

                if (options._requestHeaders?.Count > 0) // use field to avoid lazily initializing the collection
                {
                    foreach (string key in options.RequestHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(key, options.RequestHeaders[key]);
                    }
                }

                string? secValue = AddWebSocketHeaders(request, options);

                // Issue the request.
                CancellationTokenSource? linkedCancellation;
                CancellationTokenSource externalAndAbortCancellation;
                if (cancellationToken.CanBeCanceled) // avoid allocating linked source if external token is not cancelable
                {
                    linkedCancellation =
                        externalAndAbortCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abortSource.Token);
                }
                else
                {
                    linkedCancellation = null;
                    externalAndAbortCancellation = _abortSource;
                }

                using (linkedCancellation)
                {
                    response = await request.SendAsync(options.Cookies, externalAndAbortCancellation.Token).ConfigureAwait(false);
                    externalAndAbortCancellation.Token.ThrowIfCancellationRequested(); // poll in case sends/receives in request/response didn't observe cancellation
                }

                ValidateResponse(response, secValue);

                // The SecWebSocketProtocol header is optional.  We should only get it with a non-empty value if we requested subprotocols,
                // and then it must only be one of the ones we requested.  If we got a subprotocol other than one we requested (or if we
                // already got one in a previous header), fail. Otherwise, track which one we got.
                string? subprotocol = null;
                if (response.Headers.TryGetValues(HttpKnownHeaderNames.SecWebSocketProtocol, out IEnumerable<string>? subprotocolEnumerableValues))
                {
                    Debug.Assert(subprotocolEnumerableValues is string[]);
                    string[] subprotocolArray = (string[])subprotocolEnumerableValues;
                    if (subprotocolArray.Length > 0 && !string.IsNullOrEmpty(subprotocolArray[0]))
                    {
                        if (options._requestedSubProtocols is not null)
                        {
                            foreach (string requestedProtocol in options._requestedSubProtocols)
                            {
                                if (requestedProtocol.Equals(subprotocolArray[0], StringComparison.OrdinalIgnoreCase))
                                {
                                    subprotocol = requestedProtocol;
                                    break;
                                }
                            }
                        }

                        if (subprotocol == null)
                        {
                            throw new WebSocketException(
                                WebSocketError.UnsupportedProtocol,
                                SR.Format(SR.net_WebSockets_AcceptUnsupportedProtocol, string.Join(", ", options.RequestedSubProtocols), string.Join(", ", subprotocolArray)));
                        }
                    }
                }

                // Because deflate options are negotiated we need a new object
                WebSocketDeflateOptions? negotiatedDeflateOptions = null;

                if (options.DangerousDeflateOptions is not null && response.Headers.TryGetValues(HttpKnownHeaderNames.SecWebSocketExtensions, out IEnumerable<string>? extensions))
                {
                    foreach (string extension in extensions)
                    {
                        if (extension.AsSpan().TrimStart().StartsWith(ClientWebSocketDeflateConstants.Extension))
                        {
                            negotiatedDeflateOptions = ParseDeflateOptions(extension, options.DangerousDeflateOptions);
                            break;
                        }
                    }
                }

                // Get the response stream and wrap it in a web socket.
                Stream connectedStream = response.Content;
                Debug.Assert(connectedStream.CanWrite);
                Debug.Assert(connectedStream.CanRead);
                WebSocket = WebSocket.CreateFromStream(connectedStream, new WebSocketCreationOptions
                {
                    IsServer = false,
                    SubProtocol = subprotocol,
                    KeepAliveInterval = options.KeepAliveInterval,
                    KeepAliveTimeout = options.KeepAliveTimeout,
                    DangerousDeflateOptions = negotiatedDeflateOptions
                });
                _negotiatedDeflateOptions = negotiatedDeflateOptions;
            }
            catch (Exception exc)
            {
                if (_state < WebSocketState.Closed)
                {
                    _state = WebSocketState.Closed;
                }

                Abort();
                disposeResponse = true;

                if (exc is WebSocketException ||
                    (exc is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    throw;
                }

                throw new WebSocketException(WebSocketError.Faulted, SR.net_webstatus_ConnectFailure, exc);
            }
            finally
            {
                if (response is not null)
                {
                    if (options.CollectHttpResponseDetails)
                    {
                        HttpResponseHeaders = response.Headers.AsReadOnly();
                    }

                    if (disposeResponse)
                    {
                        response.Dispose();
                    }
                }
            }
        }

        // NetWasm: what the HTTP/1.1 exchange over a Socket cannot do.
        private static void ThrowIfUnsupported(Uri uri, ClientWebSocketOptions options)
        {
            if (uri.Scheme == UriScheme.Wss)
            {
                throw new PlatformNotSupportedException("wss:// needs TLS, which NetWasm does not provide yet; use ws://.");
            }

            if (options.Proxy is not null && options.Proxy != DefaultWebProxy.Instance)
            {
                throw new PlatformNotSupportedException("A WebSocket proxy is not supported on NetWasm.");
            }

            if (options.Credentials is not null || options.UseDefaultCredentials)
            {
                throw new PlatformNotSupportedException("WebSocket HTTP authentication is not supported on NetWasm; set an Authorization request header instead.");
            }

            if (options.DangerousDeflateOptions is not null)
            {
                throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);
            }
        }

        private static WebSocketDeflateOptions ParseDeflateOptions(ReadOnlySpan<char> extension, WebSocketDeflateOptions original)
        {
            var options = new WebSocketDeflateOptions();

            while (true)
            {
                int end = extension.IndexOf(';');
                ReadOnlySpan<char> value = (end >= 0 ? extension[..end] : extension).Trim();

                if (value.Length > 0)
                {
                    if (value.SequenceEqual(ClientWebSocketDeflateConstants.ClientNoContextTakeover))
                    {
                        options.ClientContextTakeover = false;
                    }
                    else if (value.SequenceEqual(ClientWebSocketDeflateConstants.ServerNoContextTakeover))
                    {
                        options.ServerContextTakeover = false;
                    }
                    else if (value.StartsWith(ClientWebSocketDeflateConstants.ClientMaxWindowBits))
                    {
                        options.ClientMaxWindowBits = ParseWindowBits(value);
                    }
                    else if (value.StartsWith(ClientWebSocketDeflateConstants.ServerMaxWindowBits))
                    {
                        options.ServerMaxWindowBits = ParseWindowBits(value);
                    }

                    static int ParseWindowBits(ReadOnlySpan<char> value)
                    {
                        var startIndex = value.IndexOf('=');

                        if (startIndex < 0 ||
                            !int.TryParse(value.Slice(startIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int windowBits) ||
                            windowBits < WebSocketValidate.MinDeflateWindowBits ||
                            windowBits > WebSocketValidate.MaxDeflateWindowBits)
                        {
                            throw new WebSocketException(WebSocketError.HeaderError,
                                SR.Format(SR.net_WebSockets_InvalidResponseHeader, ClientWebSocketDeflateConstants.Extension, value.ToString()));
                        }

                        return windowBits;
                    }
                }

                if (end < 0)
                {
                    break;
                }
                extension = extension[(end + 1)..];
            }

            if (options.ClientMaxWindowBits > original.ClientMaxWindowBits)
            {
                throw new WebSocketException(SR.Format(SR.net_WebSockets_ClientWindowBitsNegotiationFailure,
                    original.ClientMaxWindowBits, options.ClientMaxWindowBits));
            }

            if (options.ServerMaxWindowBits > original.ServerMaxWindowBits)
            {
                throw new WebSocketException(SR.Format(SR.net_WebSockets_ServerWindowBitsNegotiationFailure,
                    original.ServerMaxWindowBits, options.ServerMaxWindowBits));
            }

            return options;
        }

        /// <summary>Adds the necessary headers for the web socket request.</summary>
        /// <param name="request">The request to which the headers should be added.</param>
        /// <param name="options">The options controlling the request.</param>
        private static string? AddWebSocketHeaders(UpgradeRequest request, ClientWebSocketOptions options)
        {
            // Create the security key and expected response, then build all of the request headers
            KeyValuePair<string, string> secKeyAndSecWebSocketAccept = CreateSecKeyAndSecWebSocketAccept();
            string? secValue = secKeyAndSecWebSocketAccept.Value;
            request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.Connection, HttpKnownHeaderNames.Upgrade);
            request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.Upgrade, "websocket");
            request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.SecWebSocketKey, secKeyAndSecWebSocketAccept.Key);

            request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.SecWebSocketVersion, "13");

            if (options._requestedSubProtocols?.Count > 0)
            {
                request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.SecWebSocketProtocol, string.Join(", ", options.RequestedSubProtocols));
            }
            if (options.DangerousDeflateOptions is not null)
            {
                request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.SecWebSocketExtensions, GetDeflateOptions(options.DangerousDeflateOptions));

                static string GetDeflateOptions(WebSocketDeflateOptions options)
                {
                    var builder = new StringBuilder(ClientWebSocketDeflateConstants.MaxExtensionLength);
                    builder.Append(ClientWebSocketDeflateConstants.Extension).Append("; ");

                    if (options.ClientMaxWindowBits != WebSocketValidate.MaxDeflateWindowBits)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"{ClientWebSocketDeflateConstants.ClientMaxWindowBits}={options.ClientMaxWindowBits}");
                    }
                    else
                    {
                        // Advertise that we support this option
                        builder.Append(ClientWebSocketDeflateConstants.ClientMaxWindowBits);
                    }

                    if (!options.ClientContextTakeover)
                    {
                        builder.Append("; ").Append(ClientWebSocketDeflateConstants.ClientNoContextTakeover);
                    }

                    if (options.ServerMaxWindowBits != WebSocketValidate.MaxDeflateWindowBits)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"; {ClientWebSocketDeflateConstants.ServerMaxWindowBits}={options.ServerMaxWindowBits}");
                    }

                    if (!options.ServerContextTakeover)
                    {
                        builder.Append("; ").Append(ClientWebSocketDeflateConstants.ServerNoContextTakeover);
                    }

                    Debug.Assert(builder.Length <= ClientWebSocketDeflateConstants.MaxExtensionLength);
                    return builder.ToString();
                }
            }
            return secValue;
        }

        private static void ValidateResponse(UpgradeResponse response, string? secValue)
        {
            if (response.StatusCode != UpgradeResponse.SwitchingProtocols)
            {
                throw new WebSocketException(WebSocketError.NotAWebSocket, SR.Format(SR.net_WebSockets_ConnectStatusExpected, response.StatusCode, UpgradeResponse.SwitchingProtocols));
            }

            Debug.Assert(secValue != null);

            // The Connection, Upgrade, and SecWebSocketAccept headers are required and with specific values.
            ValidateHeader(response.Headers, HttpKnownHeaderNames.Connection, "Upgrade");
            ValidateHeader(response.Headers, HttpKnownHeaderNames.Upgrade, "websocket");
            ValidateHeader(response.Headers, HttpKnownHeaderNames.SecWebSocketAccept, secValue);
        }

        /// <summary>
        /// Creates a pair of a security key for sending in the Sec-WebSocket-Key header and
        /// the associated response we expect to receive as the Sec-WebSocket-Accept header value.
        /// </summary>
        /// <returns>A key-value pair of the request header security key and expected response header value.</returns>
        [SuppressMessage("Microsoft.Security", "CA5350", Justification = "Required by RFC6455")]
        private static unsafe KeyValuePair<string, string> CreateSecKeyAndSecWebSocketAccept()
        {
            // GUID appended by the server as part of the security key response.  Defined in the RFC.
            ReadOnlySpan<byte> wsServerGuidBytes = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"u8;

            Span<byte> bytes = stackalloc byte[24 /* Base64 guid length */ + wsServerGuidBytes.Length];

            // Base64-encode a new Guid's bytes to get the security key
            bool success = Guid.NewGuid().TryWriteBytes(bytes);
            Debug.Assert(success);
            string secKey = Convert.ToBase64String(bytes.Slice(0, 16 /*sizeof(Guid)*/));

            // Get the corresponding ASCII bytes for seckey+wsServerGuidBytes
            int encodedSecKeyLength = Encoding.ASCII.GetBytes(secKey, bytes);
            wsServerGuidBytes.CopyTo(bytes.Slice(encodedSecKeyLength));

            // Hash the seckey+wsServerGuidBytes bytes
            SHA1.TryHashData(bytes, bytes, out int bytesWritten);
            Debug.Assert(bytesWritten == 20 /* SHA1 hash length */);

            // Return the security key + the base64 encoded hashed bytes
            return new KeyValuePair<string, string>(
                secKey,
                Convert.ToBase64String(bytes.Slice(0, bytesWritten)));
        }

        private static void ValidateHeader(UpgradeHeaders headers, string name, string expectedValue)
        {
            if (headers.TryGetValues(name, out IEnumerable<string>? values))
            {
                string[] hsv = (string[])values;
                if (hsv.Length == 1)
                {
                    foreach (string value in hsv)
                    {
                        if (string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        break;
                    }
                }

                throw new WebSocketException(WebSocketError.HeaderError, SR.Format(SR.net_WebSockets_InvalidResponseHeader, name, string.Join(", ", hsv)));
            }

            throw new WebSocketException(WebSocketError.Faulted, SR.Format(SR.net_WebSockets_MissingResponseHeader, name));
        }

        /// <summary>Used as a sentinel to indicate that ClientWebSocket should use the system's default proxy.</summary>
        internal sealed class DefaultWebProxy : IWebProxy
        {
            public static DefaultWebProxy Instance { get; } = new DefaultWebProxy();
            public ICredentials? Credentials { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public Uri? GetProxy(Uri destination) => throw new NotSupportedException();
            public bool IsBypassed(Uri host) => throw new NotSupportedException();
        }

        // NetWasm: a request's or response's headers in order, by case-insensitive name.
        private sealed class UpgradeHeaders
        {
            private readonly List<KeyValuePair<string, string>> _entries = [];

            public IEnumerable<KeyValuePair<string, string>> Entries => _entries;

            public bool TryAddWithoutValidation(string name, string? value)
            {
                _entries.Add(new KeyValuePair<string, string>(name, value ?? string.Empty));
                return true;
            }

            public bool TryGetValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
            {
                var found = new List<string>();
                foreach (var entry in _entries)
                {
                    if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(entry.Value);
                    }
                }

                values = found.Count == 0 ? null : found.ToArray();
                return values is not null;
            }

            public IReadOnlyDictionary<string, IEnumerable<string>> AsReadOnly()
            {
                var headers = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in _entries)
                {
                    if (!headers.ContainsKey(entry.Key) && TryGetValues(entry.Key, out var values))
                    {
                        headers[entry.Key] = values;
                    }
                }

                return new ReadOnlyDictionary<string, IEnumerable<string>>(headers);
            }
        }

        // NetWasm: the HTTP/1.1 upgrade request SocketsHttpHandler sends upstream: it connects a TCP
        // socket, adds the Host header and the cookie container's cookies, writes the request as
        // Latin-1, reads the response head one byte at a time (so no WebSocket frame after it is
        // consumed, up to SocketsHttpHandler's 64 KB default limit) and stores Set-Cookie values.
        private sealed class UpgradeRequest(Uri uri)
        {
            private const int MaxResponseHeadersLength = 64 * 1024;

            public UpgradeHeaders Headers { get; } = new UpgradeHeaders();

            public async Task<UpgradeResponse> SendAsync(CookieContainer? cookies, CancellationToken cancellationToken)
            {
                var httpUri = new UriBuilder(uri) { Scheme = UriScheme.Http }.Uri;
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                Stream stream;
                try
                {
                    await socket.ConnectAsync(new DnsEndPoint(uri.IdnHost, uri.Port), cancellationToken).ConfigureAwait(false);
                    stream = new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }

                try
                {
                    await stream.WriteAsync(Encode(httpUri, cookies), cancellationToken).ConfigureAwait(false);
                    var response = await ReadResponseAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (cookies is not null && response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                    {
                        foreach (var value in setCookies)
                        {
                            try
                            {
                                cookies.SetCookies(httpUri, value);
                            }
                            catch (CookieException)
                            {
                                // A malformed cookie is ignored, as SocketsHttpHandler ignores it.
                            }
                        }
                    }

                    return response;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }

            private byte[] Encode(Uri httpUri, CookieContainer? cookies)
            {
                var text = new StringBuilder();
                text.Append("GET ").Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
                text.Append("Host: ").Append(uri.GetComponents(UriComponents.Host | UriComponents.Port, UriFormat.UriEscaped)).Append("\r\n");
                if (cookies?.GetCookieHeader(httpUri) is { Length: > 0 } cookieHeader)
                {
                    text.Append("Cookie: ").Append(cookieHeader).Append("\r\n");
                }

                foreach (var entry in Headers.Entries)
                {
                    text.Append(entry.Key).Append(": ").Append(entry.Value).Append("\r\n");
                }

                text.Append("\r\n");
                var bytes = new byte[text.Length];
                for (var index = 0; index < text.Length; index++)
                {
                    bytes[index] = text[index] <= 0xFF
                        ? (byte)text[index]
                        : throw new FormatException("A WebSocket request header contains a character outside Latin-1.");
                }

                return bytes;
            }

            private static async Task<UpgradeResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
            {
                var bytes = new List<byte>();
                var one = new byte[1];
                while (bytes.Count < 4 || bytes[^1] != '\n' || bytes[^2] != '\r' || bytes[^3] != '\n' || bytes[^4] != '\r')
                {
                    if (bytes.Count == MaxResponseHeadersLength)
                    {
                        throw new IOException("The WebSocket upgrade response headers exceed 64 KB.");
                    }

                    if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
                    {
                        throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
                    }

                    bytes.Add(one[0]);
                }

                var characters = new char[bytes.Count];
                for (var index = 0; index < characters.Length; index++)
                {
                    characters[index] = (char)bytes[index];
                }

                var lines = new string(characters).Split("\r\n");
                var status = lines[0].Split(' ', 3);
                if (status.Length < 2 || !status[0].StartsWith("HTTP/1.", StringComparison.Ordinal) ||
                    !int.TryParse(status[1], NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode))
                {
                    throw new IOException("The WebSocket upgrade response is not an HTTP/1.1 response.");
                }

                var response = new UpgradeResponse(statusCode, stream);
                for (var index = 1; index < lines.Length && lines[index].Length != 0; index++)
                {
                    var colon = lines[index].IndexOf(':');
                    if (colon <= 0)
                    {
                        throw new IOException("The WebSocket upgrade response has a malformed header.");
                    }

                    response.Headers.TryAddWithoutValidation(lines[index][..colon].Trim(), lines[index][(colon + 1)..].Trim());
                }

                return response;
            }
        }

        // NetWasm: the upgrade response; its content is the connection.
        private sealed class UpgradeResponse(int statusCode, Stream content) : IDisposable
        {
            public const int SwitchingProtocols = 101;

            public int StatusCode { get; } = statusCode;

            public UpgradeHeaders Headers { get; } = new UpgradeHeaders();

            public Stream Content { get; } = content;

            public void Dispose() => Content.Dispose();
        }
    }
}
