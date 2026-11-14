// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.WasiHttp;

// NetWasm: sends an HttpRequestMessage through wasi:http 0.3's client. The request's body is
// buffered, as upstream's WASI interop sends it; the response's body streams.
internal sealed class WasiHttpTransport : IHttpTransport
{
    private readonly IWasiHttpClient _client;
    private readonly IHttpFailureMapper _failureMapper;

    internal WasiHttpTransport(IWasiHttpClient client, IHttpFailureMapper failureMapper)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _failureMapper = failureMapper ?? throw new ArgumentNullException(nameof(failureMapper));
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri requestUri = request.RequestUri
            ?? throw new InvalidOperationException("A request URI is required.");
        if (!requestUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("The request URI must be absolute.");
        }

        HttpResponseMessage? response = null;
        WasiHttpResponse? incoming = null;
        try
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var headers = new List<KeyValuePair<string, byte[]>>();
            AppendHeaders(headers, request.Headers);
            if (request.Content is not null)
            {
                AppendHeaders(headers, request.Content.Headers);
            }

            incoming = await _client.SendAsync(
                new WasiHttpRequest(
                    request.Method.Method,
                    requestUri.Scheme,
                    requestUri.Authority,
                    requestUri.PathAndQuery,
                    headers,
                    body),
                cancellationToken).ConfigureAwait(false);

            response = new HttpResponseMessage((HttpStatusCode)incoming.StatusCode)
            {
                Content = new StreamContent(incoming.Body),
            };
            var received = incoming.Headers;
            incoming = null;

            // As .NET's handlers take a received header: as sent, without validating its value (an
            // unquoted ETag stays readable), and on the content when it is a content header.
            foreach (var header in received)
            {
                if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return response;
        }
        catch (WasiHttpTransportFailureException failure)
        {
            response?.Dispose();
            throw _failureMapper.Translate(failure.Facts);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
        finally
        {
            incoming?.Body.Dispose();
        }
    }

    private static void AppendHeaders(List<KeyValuePair<string, byte[]>> headers, HttpHeaders source)
    {
        foreach (var header in source)
        {
            foreach (var value in header.Value)
            {
                headers.Add(new KeyValuePair<string, byte[]>(header.Key, Encoding.UTF8.GetBytes(value)));
            }
        }
    }
}
