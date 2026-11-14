// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Net.Http.WasiHttp;

// NetWasm: a wasi:http error code as the HttpRequestError .NET reports for the same failure.
internal sealed class WasiHttpFailureMapper : IHttpFailureMapper
{
    public HttpRequestException Translate(WasiHttpFailureFacts failure) =>
        new(MapError(failure.Kind), failure.Message ?? $"WASI HTTP request failed: {failure.Kind}.");

    private static HttpRequestError MapError(WasiHttpFailureKind kind) => kind switch
    {
        WasiHttpFailureKind.DnsTimeout or WasiHttpFailureKind.DnsError =>
            HttpRequestError.NameResolutionError,
        WasiHttpFailureKind.TlsProtocolError or WasiHttpFailureKind.TlsCertificateError or
            WasiHttpFailureKind.TlsAlertReceived => HttpRequestError.SecureConnectionError,
        WasiHttpFailureKind.HttpRequestMethodInvalid or WasiHttpFailureKind.HttpRequestUriInvalid or
            WasiHttpFailureKind.HttpRequestUriTooLong or WasiHttpFailureKind.HttpRequestHeaderSectionSize or
            WasiHttpFailureKind.HttpRequestHeaderSize or WasiHttpFailureKind.HttpRequestTrailerSectionSize or
            WasiHttpFailureKind.HttpRequestTrailerSize or WasiHttpFailureKind.HttpResponseHeaderSectionSize or
            WasiHttpFailureKind.HttpResponseHeaderSize or WasiHttpFailureKind.HttpResponseTrailerSectionSize or
            WasiHttpFailureKind.HttpResponseTrailerSize or WasiHttpFailureKind.HttpResponseTransferCoding or
            WasiHttpFailureKind.HttpResponseContentCoding or WasiHttpFailureKind.HttpProtocolError or
            WasiHttpFailureKind.HttpResponseIncomplete or WasiHttpFailureKind.HttpResponseBodySize or
            WasiHttpFailureKind.HttpRequestBodySize or WasiHttpFailureKind.HttpRequestLengthRequired =>
            HttpRequestError.InvalidResponse,
        // .NET has no configuration error; upstream's WASI handler reports CONFIGURATION_ERROR by name.
        WasiHttpFailureKind.ConfigurationError or WasiHttpFailureKind.InternalError => HttpRequestError.Unknown,
        _ => HttpRequestError.ConnectionError,
    };
}
