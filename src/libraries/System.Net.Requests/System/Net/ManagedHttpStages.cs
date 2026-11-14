// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime 811225a4's SocketsHttpHandler RedirectHandler, its cookie handling (HttpConnection's Cookie
// header from the container and CookieHelper.ProcessReceivedCookies) and the HttpUtilities scheme checks, as delegating
// handlers over the WASI handler, which follows no redirects and keeps no cookies. HttpWebRequest composes them as
// SocketsHttpHandler does: redirects outermost, with cookies applied on every hop. Tracing and telemetry are left out.

using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net
{
    internal sealed class ManagedRedirectHandler : DelegatingHandler
    {
        private readonly int _maxAutomaticRedirections;

        public ManagedRedirectHandler(HttpMessageHandler innerHandler, int maxAutomaticRedirections)
            : base(innerHandler)
        {
            Debug.Assert(maxAutomaticRedirections > 0);

            _maxAutomaticRedirections = maxAutomaticRedirections;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            uint redirectCount = 0;
            Uri? redirectUri;
            Debug.Assert(request.RequestUri != null);
            while ((redirectUri = GetUriForRedirect(request.RequestUri, response)) != null)
            {
                redirectCount++;

                if (redirectCount > _maxAutomaticRedirections)
                {
                    // If we exceed the maximum number of redirects
                    // then just return the 3xx response.
                    break;
                }

                response.Dispose();

                // Clear the authorization header.
                request.Headers.Authorization = null;

                // Set up for the redirect
                request.RequestUri = redirectUri;
                if (RequestRequiresForceGet(response.StatusCode, request.Method))
                {
                    request.Method = HttpMethod.Get;
                    request.Content = null;
                    if (request.Headers.TransferEncodingChunked == true)
                    {
                        request.Headers.TransferEncodingChunked = false;
                    }
                }

                // Issue the redirected request.
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        private static Uri? GetUriForRedirect(Uri requestUri, HttpResponseMessage response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Moved:
                case HttpStatusCode.Found:
                case HttpStatusCode.SeeOther:
                case HttpStatusCode.TemporaryRedirect:
                case HttpStatusCode.MultipleChoices:
                case HttpStatusCode.PermanentRedirect:
                    break;

                default:
                    return null;
            }

            Uri? location = response.Headers.Location;
            if (location == null)
            {
                return null;
            }

            // Ensure the redirect location is an absolute URI.
            if (!location.IsAbsoluteUri)
            {
                location = new Uri(requestUri, location);
            }

            // Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a
            // fragment should inherit the fragment from the original URI.
            string requestFragment = requestUri.Fragment;
            if (!string.IsNullOrEmpty(requestFragment))
            {
                string redirectFragment = location.Fragment;
                if (string.IsNullOrEmpty(redirectFragment))
                {
                    location = new UriBuilder(location) { Fragment = requestFragment }.Uri;
                }
            }

            // Disallow automatic redirection from secure to non-secure schemes
            if (IsSupportedSecureScheme(requestUri.Scheme) && !IsSupportedSecureScheme(location.Scheme))
            {
                return null;
            }

            // Disallow automatic redirection to unsupported schemes
            if (!IsSupportedScheme(location.Scheme))
            {
                return null;
            }

            return location;
        }

        private static bool RequestRequiresForceGet(HttpStatusCode statusCode, HttpMethod requestMethod)
        {
            switch (statusCode)
            {
                case HttpStatusCode.Moved:
                case HttpStatusCode.Found:
                case HttpStatusCode.MultipleChoices:
                    return requestMethod == HttpMethod.Post;
                case HttpStatusCode.SeeOther:
                    return requestMethod != HttpMethod.Get && requestMethod != HttpMethod.Head;
                default:
                    return false;
            }
        }

        private static bool IsSupportedScheme(string scheme) =>
            IsSupportedNonSecureScheme(scheme) ||
            IsSupportedSecureScheme(scheme);

        private static bool IsSupportedNonSecureScheme(string scheme) =>
            string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "ws", StringComparison.OrdinalIgnoreCase);

        private static bool IsSupportedSecureScheme(string scheme) =>
            string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class ContainerCookieHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookieContainer;
        private string[]? _requestCookies;
        private bool _requestCookiesCaptured;

        public ContainerCookieHandler(HttpMessageHandler innerHandler, CookieContainer cookieContainer)
            : base(innerHandler)
        {
            _cookieContainer = cookieContainer;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Debug.Assert(request.RequestUri != null);

            // The request's own Cookie values, kept from the first hop so that each redirect hop starts from them.
            if (!_requestCookiesCaptured)
            {
                _requestCookiesCaptured = true;
                if (request.Headers.TryGetValues(HttpKnownHeaderNames.Cookie, out IEnumerable<string>? values))
                {
                    _requestCookies = new List<string>(values).ToArray();
                }
            }

            // As HttpConnection writes them: the container's cookies for this hop follow the first Cookie value,
            // separated by "; ", or form the Cookie header when the request carries none.
            string cookiesFromContainer = _cookieContainer.GetCookieHeader(request.RequestUri);
            request.Headers.Remove(HttpKnownHeaderNames.Cookie);
            if (_requestCookies is { Length: > 0 } requestCookies)
            {
                request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.Cookie,
                    cookiesFromContainer.Length == 0 ? requestCookies[0] : requestCookies[0] + "; " + cookiesFromContainer);
                for (int i = 1; i < requestCookies.Length; i++)
                {
                    request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.Cookie, requestCookies[i]);
                }
            }
            else if (cookiesFromContainer.Length != 0)
            {
                request.Headers.TryAddWithoutValidation(HttpKnownHeaderNames.Cookie, cookiesFromContainer);
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            ProcessReceivedCookies(response, request.RequestUri);
            return response;
        }

        private void ProcessReceivedCookies(HttpResponseMessage response, Uri requestUri)
        {
            if (response.Headers.TryGetValues(HttpKnownHeaderNames.SetCookie, out IEnumerable<string>? values))
            {
                foreach (string value in values)
                {
                    try
                    {
                        _cookieContainer.SetCookies(requestUri, value);
                    }
                    catch (CookieException)
                    {
                        // Ignore invalid Set-Cookie header and continue processing.
                    }
                }
            }
        }
    }
}
