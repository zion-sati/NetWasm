// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: what WasiHttpHandler's chain needs of SocketsHttpHandler's connection layer, over IHttpTransport.
// wasi:http owns connections, so there is no pooling, protocol selection or connection-level (NTLM, Negotiate)
// authentication here: a pool is the per-origin state SocketsHttpHandler keeps beside its connections, the credentials
// PreAuthenticate reuses.

using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.WasiHttp;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    // SocketsHttpHandler's settings that WasiHttpHandler implements, with SocketsHttpHandler's defaults.
    internal sealed class WasiHttpSettings
    {
        internal bool UseCookies = HttpHandlerDefaults.DefaultUseCookies;
        internal CookieContainer? CookieContainer;
        internal DecompressionMethods AutomaticDecompression = HttpHandlerDefaults.DefaultAutomaticDecompression;
        internal bool PreAuthenticate = HttpHandlerDefaults.DefaultPreAuthenticate;
        internal ICredentials? Credentials;
        // AuthenticationHelper's send, set with Credentials: only a handler given credentials reaches it (and Digest's
        // hashing), as only one given AutomaticDecompression reaches DecompressionHandler.
        internal Func<HttpRequestMessage, bool, ICredentials, bool, HttpConnectionPool, CancellationToken, ValueTask<HttpResponseMessage>>? AuthenticatedSend;
        internal bool AllowAutoRedirect = HttpHandlerDefaults.DefaultAutomaticRedirection;
        internal int MaxAutomaticRedirections = HttpHandlerDefaults.DefaultMaxAutomaticRedirections;
        internal int MaxResponseHeadersLength = HttpHandlerDefaults.DefaultMaxResponseHeadersLength;

        // As SocketsHttpHandler's: the limit is set in kilobytes.
        internal int MaxResponseHeadersByteLength => (int)Math.Min(int.MaxValue, MaxResponseHeadersLength * 1024L);

        // The view a started handler sends with. As SocketsHttpHandler's CloneAndNormalize, the cookie container is
        // created first, so the handler's CookieContainer and the clone share it.
        internal WasiHttpSettings Clone()
        {
            if (UseCookies)
            {
                CookieContainer ??= new CookieContainer();
            }
            return (WasiHttpSettings)MemberwiseClone();
        }
    }

    // SocketsHttpHandler's HttpConnectionHandler: each request goes to its origin's pool, through AuthenticationHelper
    // when the handler has credentials.
    internal sealed class WasiHttpConnectionHandler(IHttpTransport transport, WasiHttpSettings settings) : HttpMessageHandlerStage
    {
        private readonly Dictionary<string, HttpConnectionPool> _pools = new(StringComparer.OrdinalIgnoreCase);

        internal override ValueTask<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool async, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri ?? throw new InvalidOperationException(SR.net_http_client_invalid_requesturi);
            HttpConnectionPool pool = GetPool(uri);
            return settings.Credentials is { } credentials && settings.AuthenticatedSend is { } authenticatedSend
                ? authenticatedSend(request, async, credentials, settings.PreAuthenticate, pool, cancellationToken)
                : pool.SendWithVersionDetectionAndRetryAsync(request, async, doRequestAuth: false, cancellationToken);
        }

        private HttpConnectionPool GetPool(Uri uri)
        {
            string origin = uri.IsAbsoluteUri ? uri.GetLeftPart(UriPartial.Authority) : string.Empty;
            lock (_pools)
            {
                if (!_pools.TryGetValue(origin, out HttpConnectionPool? pool))
                {
                    pool = new HttpConnectionPool(transport, settings);
                    _pools.Add(origin, pool);
                }
                return pool;
            }
        }
    }

    // The per-origin state AuthenticationHelper asks SocketsHttpHandler's pool for, and the pool's send: cookies from
    // the container go out with the request's own, as SocketsHttpHandler writes them, the response header limit is
    // checked, and received cookies are stored.
    internal sealed class HttpConnectionPool(IHttpTransport transport, WasiHttpSettings settings)
    {
        public PreAuthCredentialCache? PreAuthCredentials { get; } = settings.PreAuthenticate ? new PreAuthCredentialCache() : null;

        public ValueTask<HttpResponseMessage> SendWithVersionDetectionAndRetryAsync(HttpRequestMessage request, bool async, bool doRequestAuth, CancellationToken cancellationToken) =>
            SendAsync(request, async, cancellationToken);

        // The host chooses the route, so there is no proxy to authenticate with.
        public ValueTask<HttpResponseMessage> SendWithProxyAuthAsync(HttpRequestMessage request, bool async, bool doRequestAuth, CancellationToken cancellationToken) =>
            SendAsync(request, async, cancellationToken);

        private async ValueTask<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool async, CancellationToken cancellationToken)
        {
            Debug.Assert(request.RequestUri is not null);
            string? cookies = settings.UseCookies ? settings.CookieContainer!.GetCookieHeader(request.RequestUri) : null;
            string[]? requestCookies = null;
            if (!string.IsNullOrEmpty(cookies))
            {
                // The request keeps its own Cookie header; the container's cookies follow it for this send only.
                if (request.Headers.TryGetValues("Cookie", out IEnumerable<string>? values))
                {
                    requestCookies = new List<string>(values).ToArray();
                    request.Headers.Remove("Cookie");
                    cookies = string.Join("; ", requestCookies) + "; " + cookies; // Cookies use "; " as the separator
                }
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

            HttpResponseMessage response;
            try
            {
                response = async
                    ? await transport.SendAsync(request, cancellationToken).ConfigureAwait(false)
                    : System.Runtime.InteropServices.WebAssembly.CanonicalAbi.RunSynchronously(() => transport.SendAsync(request, cancellationToken));
            }
            finally
            {
                if (cookies is not null)
                {
                    request.Headers.Remove("Cookie");
                    if (requestCookies is not null)
                    {
                        request.Headers.TryAddWithoutValidation("Cookie", requestCookies);
                    }
                }
            }

            // As upstream's WASI request wrapper does, the response names the request it answers.
            response.RequestMessage = request;

            if (HeaderLength(response) > settings.MaxResponseHeadersByteLength)
            {
                response.Dispose();
                throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded,
                    SR.Format(SR.net_http_response_headers_exceeded_length, settings.MaxResponseHeadersByteLength));
            }

            if (settings.UseCookies)
            {
                CookieHelper.ProcessReceivedCookies(response, settings.CookieContainer!);
            }

            return response;
        }

        // The bytes the response's header lines take on the wire, "name: value\r\n" each, which SocketsHttpHandler
        // counts against the limit as it reads them.
        private static long HeaderLength(HttpResponseMessage response) =>
            HeaderLength(response.Headers) + HeaderLength(response.Content.Headers);

        private static long HeaderLength(HttpHeaders headers)
        {
            long length = 0;
            foreach (KeyValuePair<string, HeaderStringValues> header in headers.NonValidated)
            {
                foreach (string value in header.Value)
                {
                    length += header.Key.Length + value.Length + 4;
                }
            }
            return length;
        }
    }
}
