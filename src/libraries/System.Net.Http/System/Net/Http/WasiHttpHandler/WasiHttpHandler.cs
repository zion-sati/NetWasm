// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime deae2e32's (.NET 11.0.0) WasiHttpHandler surface, sending through NetWasm's IHttpTransport
// rather than upstream's own WASI bindings. The default transport sends through wasi:http 0.3; HttpClientHandler's
// transport constructor supplies another. Unlike upstream's, a synchronous Send works (owner decision): it blocks as
// NetWasm's synchronous file and socket I/O does.
//
// Upstream's WASI handler throws PlatformNotSupportedException from every policy control. NetWasm implements the ones
// SocketsHttpHandler implements in managed code, with its own stages and defaults: redirects (RedirectHandler),
// decompression (DecompressionHandler), cookies (CookieHelper), Basic and Digest credentials (AuthenticationHelper)
// and the response header limit. The host owns what wasi:http does not expose, so proxies, connection limits and TLS
// keep upstream's PlatformNotSupportedException.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.WasiHttp;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    internal sealed class WasiHttpHandler : HttpMessageHandler
    {
        // SocketsHttpHandler's methods, without Zstandard (8), whose codec NetWasm does not build: it is never
        // requested. Brotli (4) has no name in netstandard 2.1's DecompressionMethods, but its value works.
        private const DecompressionMethods SupportedDecompressionMethods =
            DecompressionMethods.GZip | DecompressionMethods.Deflate | (DecompressionMethods)4;

        private readonly IHttpTransport _transport;
        private readonly WasiHttpSettings _settings = new();
        private HttpMessageHandlerStage? _handler;
        private Func<DecompressionMethods, HttpMessageHandlerStage, HttpMessageHandlerStage>? _decompressionHandlerFactory;
        private bool _disposed;

        public WasiHttpHandler()
            : this(WasiHttpTransportComposition.Create())
        {
        }

        public WasiHttpHandler(IHttpTransport transport)
        {
            ArgumentNullException.ThrowIfNull(transport);
            _transport = transport;
        }

        public const bool SupportsAutomaticDecompression = true;
        public const bool SupportsProxy = false;
        public const bool SupportsRedirectConfiguration = true;

        public IDictionary<string, object?> Properties => field ??= new Dictionary<string, object?>();

        internal ClientCertificateOption ClientCertificateOptions;

        public bool UseCookies
        {
            get => _settings.UseCookies;
            set
            {
                CheckDisposedOrStarted();
                _settings.UseCookies = value;
            }
        }

        [AllowNull]
        public CookieContainer CookieContainer
        {
            get => _settings.CookieContainer ??= new CookieContainer();
            set
            {
                CheckDisposedOrStarted();
                _settings.CookieContainer = value;
            }
        }

        public DecompressionMethods AutomaticDecompression
        {
            get => _settings.AutomaticDecompression;
            set
            {
                CheckDisposedOrStarted();
                // As SocketsHttpHandler's: only a handler that sets this reaches DecompressionHandler and the codecs.
                _decompressionHandlerFactory ??= static (methods, handler) => new DecompressionHandler(methods, handler);
                _settings.AutomaticDecompression = value;
            }
        }

        public bool PreAuthenticate
        {
            get => _settings.PreAuthenticate;
            set
            {
                CheckDisposedOrStarted();
                _settings.PreAuthenticate = value;
            }
        }

        public ICredentials? Credentials
        {
            get => _settings.Credentials;
            set
            {
                CheckDisposedOrStarted();
                _settings.AuthenticatedSend ??= static (request, async, credentials, preAuthenticate, pool, cancellationToken) =>
                    AuthenticationHelper.SendWithRequestAuthAsync(request, async, credentials, preAuthenticate, pool, cancellationToken);
                _settings.Credentials = value;
            }
        }

        public bool AllowAutoRedirect
        {
            get => _settings.AllowAutoRedirect;
            set
            {
                CheckDisposedOrStarted();
                _settings.AllowAutoRedirect = value;
            }
        }

        public int MaxAutomaticRedirections
        {
            get => _settings.MaxAutomaticRedirections;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

                CheckDisposedOrStarted();
                _settings.MaxAutomaticRedirections = value;
            }
        }

        public int MaxResponseHeadersLength
        {
            get => _settings.MaxResponseHeadersLength;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

                CheckDisposedOrStarted();
                _settings.MaxResponseHeadersLength = value;
            }
        }

        #region PlatformNotSupported
#pragma warning disable CA1822

        // wasi:http's host chooses the route, the connections and TLS.
        public bool UseProxy
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public IWebProxy? Proxy
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public ICredentials? DefaultProxyCredentials
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public int MaxConnectionsPerServer
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public SslClientAuthenticationOptions SslOptions
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }
#pragma warning restore CA1822

        #endregion

        protected internal override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            HttpMessageHandlerStage handler = _handler ?? SetupHandlerChain();
            return handler.SendAsync(request, cancellationToken);
        }

        // The transport's send runs synchronously (CanonicalAbi.RunSynchronously): each wasi:http subtask and stream
        // copy is waited for in place, and nothing else runs meanwhile.
        protected internal override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(_disposed, this);

            cancellationToken.ThrowIfCancellationRequested();

            HttpMessageHandlerStage handler = _handler ?? SetupHandlerChain();
            return handler.Send(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _handler?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void CheckDisposedOrStarted()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handler != null)
            {
                throw new InvalidOperationException(SR.net_http_operation_started);
            }
        }

        // SocketsHttpHandler's chain over the transport: the connection stage, then redirects, then decompression.
        private HttpMessageHandlerStage SetupHandlerChain()
        {
            WasiHttpSettings settings = _settings.Clone();
            HttpMessageHandlerStage handler = new WasiHttpConnectionHandler(_transport, settings);

            if (settings.AllowAutoRedirect)
            {
                // Just as with WinHttpHandler and SocketsHttpHandler, for security reasons, we do not support
                // authentication on redirects if the credential is anything other than a CredentialCache.
                handler = new RedirectHandler(settings.MaxAutomaticRedirections, handler,
                    disableAuthOnRedirect: settings.Credentials is not CredentialCache);
            }

            DecompressionMethods decompression = settings.AutomaticDecompression & SupportedDecompressionMethods;
            if (decompression != DecompressionMethods.None)
            {
                handler = _decompressionHandlerFactory!(decompression, handler);
            }

            if (Interlocked.CompareExchange(ref _handler, handler, null) != null)
            {
                handler.Dispose();
            }

            return _handler;
        }
    }
}
