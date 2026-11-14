// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading.Tasks;
using Bindings = NetWasm.Wit.Netwasm.Http._1._0._0;

namespace System.Net.Http.WasiHttp;

// NetWasm: sends a request through wasi:http/client@0.3.2's send, the async import Wasmtime and
// other WASI 0.3 hosts implement, with the bindings netwasm-wit-bindgen generates from
// client.wit (`--world netwasm:http/client@1.0.0 --accessibility internal`).
internal sealed class WasiHttpBindingClient : IWasiHttpClient
{
    public async Task<WasiHttpResponse> SendAsync(WasiHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var sent = await Bindings.ClientImports.Send(CreateRequest(request)).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            throw new WasiHttpTransportFailureException(Map(sent.Error));
        }

        return Receive(sent.Ok);
    }

    private static Bindings.Request CreateRequest(WasiHttpRequest request)
    {
        var entries = new (string, byte[])[request.Headers.Count];
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = (request.Headers[index].Key, request.Headers[index].Value);
        }

        var headers = Bindings.WasiHttpTypesImports.FromList(entries);
        if (!headers.IsOk)
        {
            throw new WasiHttpTransportFailureException(new WasiHttpFailureFacts(
                WasiHttpFailureKind.HttpRequestHeaderSize,
                "WASI HTTP rejected a request header."));
        }

        // The body is complete when sent, so it has no trailers.
        var (outgoing, _) = Bindings.WasiHttpTypesImports.RequestNew(
            headers.Ok,
            request.Body is null ? WitOption<Stream>.None : new WitOption<Stream>(new MemoryStream(request.Body)),
            Task.FromResult(WitResult<WitOption<Bindings.Fields>, Bindings.ErrorCode>.FromOk(WitOption<Bindings.Fields>.None)),
            WitOption<Bindings.RequestOptions>.None);
        try
        {
            RequireSet(
                Bindings.WasiHttpTypesImports.SetMethod(outgoing, MapMethod(request.Method)),
                WasiHttpFailureKind.HttpRequestMethodInvalid);
            RequireSet(
                Bindings.WasiHttpTypesImports.SetScheme(outgoing, new WitOption<Bindings.Scheme>(MapScheme(request.Scheme))),
                WasiHttpFailureKind.HttpRequestUriInvalid);
            RequireSet(
                Bindings.WasiHttpTypesImports.SetAuthority(outgoing, new WitOption<string>(request.Authority)),
                WasiHttpFailureKind.HttpRequestUriInvalid);
            RequireSet(
                Bindings.WasiHttpTypesImports.SetPathWithQuery(outgoing, new WitOption<string>(request.PathWithQuery)),
                WasiHttpFailureKind.HttpRequestUriInvalid);
            return outgoing;
        }
        catch
        {
            outgoing.Dispose();
            throw;
        }
    }

    // The response's status and headers, and its body as a stream; consuming the body ends the
    // response resource.
    private static WasiHttpResponse Receive(Bindings.Response response)
    {
        var status = Bindings.WasiHttpTypesImports.GetStatusCode(response);
        var headers = new List<KeyValuePair<string, string>>();
        using (var fields = Bindings.WasiHttpTypesImports.ResponseGetHeaders(response))
        {
            foreach (var (name, value) in Bindings.WasiHttpTypesImports.CopyAll(fields))
            {
                headers.Add(new KeyValuePair<string, string>(name, Encoding.UTF8.GetString(value)));
            }
        }

        var (body, _) = Bindings.WasiHttpTypesImports.ResponseConsumeBody(
            response,
            Task.FromResult(WitResult<WitUnit, Bindings.ErrorCode>.FromOk(default)));
        return new WasiHttpResponse(status, headers, body);
    }

    private static void RequireSet(WitResult<WitUnit, WitUnit> result, WasiHttpFailureKind failure)
    {
        if (!result.IsOk)
        {
            throw new WasiHttpTransportFailureException(new WasiHttpFailureFacts(failure));
        }
    }

    private static Bindings.Method MapMethod(string method) =>
        method.ToUpperInvariant() switch
        {
            "GET" => Bindings.Method.Get(),
            "HEAD" => Bindings.Method.Head(),
            "POST" => Bindings.Method.Post(),
            "PUT" => Bindings.Method.Put(),
            "DELETE" => Bindings.Method.Delete(),
            "CONNECT" => Bindings.Method.Connect(),
            "OPTIONS" => Bindings.Method.Options(),
            "TRACE" => Bindings.Method.Trace(),
            "PATCH" => Bindings.Method.Patch(),
            _ => Bindings.Method.Other(method),
        };

    private static Bindings.Scheme MapScheme(string scheme) =>
        scheme.ToLowerInvariant() switch
        {
            "http" => Bindings.Scheme.HTTP(),
            "https" => Bindings.Scheme.HTTPS(),
            _ => Bindings.Scheme.Other(scheme),
        };

    private static WasiHttpFailureFacts Map(Bindings.ErrorCode error) =>
        new(error.Tag switch
        {
            Bindings.ErrorCodeTag.DNSTimeout => WasiHttpFailureKind.DnsTimeout,
            Bindings.ErrorCodeTag.DNSError => WasiHttpFailureKind.DnsError,
            Bindings.ErrorCodeTag.DestinationNotFound => WasiHttpFailureKind.DestinationNotFound,
            Bindings.ErrorCodeTag.DestinationUnavailable => WasiHttpFailureKind.DestinationUnavailable,
            Bindings.ErrorCodeTag.DestinationIPProhibited => WasiHttpFailureKind.DestinationIpProhibited,
            Bindings.ErrorCodeTag.DestinationIPUnroutable => WasiHttpFailureKind.DestinationIpUnroutable,
            Bindings.ErrorCodeTag.ConnectionRefused => WasiHttpFailureKind.ConnectionRefused,
            Bindings.ErrorCodeTag.ConnectionTerminated => WasiHttpFailureKind.ConnectionTerminated,
            Bindings.ErrorCodeTag.ConnectionTimeout => WasiHttpFailureKind.ConnectionTimeout,
            Bindings.ErrorCodeTag.ConnectionReadTimeout => WasiHttpFailureKind.ConnectionReadTimeout,
            Bindings.ErrorCodeTag.ConnectionWriteTimeout => WasiHttpFailureKind.ConnectionWriteTimeout,
            Bindings.ErrorCodeTag.ConnectionLimitReached => WasiHttpFailureKind.ConnectionLimitReached,
            Bindings.ErrorCodeTag.TLSProtocolError => WasiHttpFailureKind.TlsProtocolError,
            Bindings.ErrorCodeTag.TLSCertificateError => WasiHttpFailureKind.TlsCertificateError,
            Bindings.ErrorCodeTag.TLSAlertReceived => WasiHttpFailureKind.TlsAlertReceived,
            Bindings.ErrorCodeTag.HTTPRequestDenied => WasiHttpFailureKind.HttpRequestDenied,
            Bindings.ErrorCodeTag.HTTPRequestLengthRequired => WasiHttpFailureKind.HttpRequestLengthRequired,
            Bindings.ErrorCodeTag.HTTPRequestBodySize => WasiHttpFailureKind.HttpRequestBodySize,
            Bindings.ErrorCodeTag.HTTPRequestMethodInvalid => WasiHttpFailureKind.HttpRequestMethodInvalid,
            Bindings.ErrorCodeTag.HTTPRequestURIInvalid => WasiHttpFailureKind.HttpRequestUriInvalid,
            Bindings.ErrorCodeTag.HTTPRequestURITooLong => WasiHttpFailureKind.HttpRequestUriTooLong,
            Bindings.ErrorCodeTag.HTTPRequestHeaderSectionSize => WasiHttpFailureKind.HttpRequestHeaderSectionSize,
            Bindings.ErrorCodeTag.HTTPRequestHeaderSize => WasiHttpFailureKind.HttpRequestHeaderSize,
            Bindings.ErrorCodeTag.HTTPRequestTrailerSectionSize => WasiHttpFailureKind.HttpRequestTrailerSectionSize,
            Bindings.ErrorCodeTag.HTTPRequestTrailerSize => WasiHttpFailureKind.HttpRequestTrailerSize,
            Bindings.ErrorCodeTag.HTTPResponseIncomplete => WasiHttpFailureKind.HttpResponseIncomplete,
            Bindings.ErrorCodeTag.HTTPResponseHeaderSectionSize => WasiHttpFailureKind.HttpResponseHeaderSectionSize,
            Bindings.ErrorCodeTag.HTTPResponseHeaderSize => WasiHttpFailureKind.HttpResponseHeaderSize,
            Bindings.ErrorCodeTag.HTTPResponseBodySize => WasiHttpFailureKind.HttpResponseBodySize,
            Bindings.ErrorCodeTag.HTTPResponseTrailerSectionSize => WasiHttpFailureKind.HttpResponseTrailerSectionSize,
            Bindings.ErrorCodeTag.HTTPResponseTrailerSize => WasiHttpFailureKind.HttpResponseTrailerSize,
            Bindings.ErrorCodeTag.HTTPResponseTransferCoding => WasiHttpFailureKind.HttpResponseTransferCoding,
            Bindings.ErrorCodeTag.HTTPResponseContentCoding => WasiHttpFailureKind.HttpResponseContentCoding,
            Bindings.ErrorCodeTag.HTTPResponseTimeout => WasiHttpFailureKind.HttpResponseTimeout,
            Bindings.ErrorCodeTag.HTTPUpgradeFailed => WasiHttpFailureKind.HttpUpgradeFailed,
            Bindings.ErrorCodeTag.HTTPProtocolError => WasiHttpFailureKind.HttpProtocolError,
            Bindings.ErrorCodeTag.LoopDetected => WasiHttpFailureKind.LoopDetected,
            Bindings.ErrorCodeTag.ConfigurationError => WasiHttpFailureKind.ConfigurationError,
            _ => WasiHttpFailureKind.InternalError,
        },
        error.Tag == Bindings.ErrorCodeTag.InternalError && error.InternalErrorValue.HasValue
            ? error.InternalErrorValue.Value
            : null);
}
