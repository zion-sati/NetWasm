// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http.WasiHttp;

// NetWasm: the managed facts of a wasi:http 0.3 exchange. Only WasiHttpBindingClient touches the
// generated bindings, so the transport and its failure mapping run on desktop .NET in tests.

// wasi:http's error-code cases.
internal enum WasiHttpFailureKind
{
    DnsTimeout,
    DnsError,
    DestinationNotFound,
    DestinationUnavailable,
    DestinationIpProhibited,
    DestinationIpUnroutable,
    ConnectionRefused,
    ConnectionTerminated,
    ConnectionTimeout,
    ConnectionReadTimeout,
    ConnectionWriteTimeout,
    ConnectionLimitReached,
    TlsProtocolError,
    TlsCertificateError,
    TlsAlertReceived,
    HttpRequestDenied,
    HttpRequestLengthRequired,
    HttpRequestBodySize,
    HttpRequestMethodInvalid,
    HttpRequestUriInvalid,
    HttpRequestUriTooLong,
    HttpRequestHeaderSectionSize,
    HttpRequestHeaderSize,
    HttpRequestTrailerSectionSize,
    HttpRequestTrailerSize,
    HttpResponseIncomplete,
    HttpResponseHeaderSectionSize,
    HttpResponseHeaderSize,
    HttpResponseBodySize,
    HttpResponseTrailerSectionSize,
    HttpResponseTrailerSize,
    HttpResponseTransferCoding,
    HttpResponseContentCoding,
    HttpResponseTimeout,
    HttpUpgradeFailed,
    HttpProtocolError,
    LoopDetected,
    ConfigurationError,
    InternalError,
}

internal readonly struct WasiHttpFailureFacts
{
    internal WasiHttpFailureFacts(WasiHttpFailureKind kind, string? message = null)
    {
        Kind = kind;
        Message = message;
    }

    internal WasiHttpFailureKind Kind { get; }
    internal string? Message { get; }
}

internal sealed class WasiHttpTransportFailureException : Exception
{
    internal WasiHttpTransportFailureException(WasiHttpFailureFacts facts)
        : base(facts.Message)
    {
        Facts = facts;
    }

    internal WasiHttpFailureFacts Facts { get; }
}

// A request as wasi:http's request resource takes it: each header value separately, as
// upstream's WASI interop converts request headers.
internal sealed class WasiHttpRequest
{
    internal WasiHttpRequest(
        string method,
        string scheme,
        string authority,
        string pathWithQuery,
        IReadOnlyList<KeyValuePair<string, byte[]>> headers,
        byte[]? body)
    {
        Method = method ?? throw new ArgumentNullException(nameof(method));
        Scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        PathWithQuery = pathWithQuery ?? throw new ArgumentNullException(nameof(pathWithQuery));
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        Body = body;
    }

    internal string Method { get; }
    internal string Scheme { get; }
    internal string Authority { get; }
    internal string PathWithQuery { get; }
    internal IReadOnlyList<KeyValuePair<string, byte[]>> Headers { get; }
    internal byte[]? Body { get; }
}

// A response whose body streams from wasi:http's response body stream.
internal sealed class WasiHttpResponse
{
    internal WasiHttpResponse(int statusCode, IReadOnlyList<KeyValuePair<string, string>> headers, Stream body)
    {
        StatusCode = statusCode;
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    internal int StatusCode { get; }
    internal IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
    internal Stream Body { get; }
}

internal interface IWasiHttpClient
{
    Task<WasiHttpResponse> SendAsync(WasiHttpRequest request, CancellationToken cancellationToken);
}

internal interface IHttpFailureMapper
{
    HttpRequestException Translate(WasiHttpFailureFacts failure);
}
