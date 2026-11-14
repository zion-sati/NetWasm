using System.Net;
using System.Net.Http;
using System.Net.Http.WasiHttp;
using System.Text;

namespace NetWasm.SystemNetHttp.Tests;

// The wasi:http 0.3 transport over a recording client: what it hands wasi:http's request, how it
// builds the response, and how wasi:http's error codes surface.
public sealed class WasiHttpTransportTests
{
    [Fact]
    public async Task SendsTheRequestAsWasiHttpTakesIt()
    {
        var client = new RecordingClient();
        var transport = new WasiHttpTransport(client, new WasiHttpFailureMapper());
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com:8443/items?id=7")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Trace", ["a", "b"]);

        using var response = await transport.SendAsync(request, default);

        var sent = Assert.IsType<WasiHttpRequest>(client.Request);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("https", sent.Scheme);
        Assert.Equal("example.com:8443", sent.Authority);
        Assert.Equal("/items?id=7", sent.PathWithQuery);
        Assert.Equal("payload", Encoding.UTF8.GetString(sent.Body!));
        Assert.Equal(
            // Buffering the content computes its length, which goes with the content headers.
            ["X-Trace=a", "X-Trace=b", "Content-Type=text/plain; charset=utf-8", "Content-Length=7"],
            sent.Headers.Select(header => header.Key + "=" + Encoding.UTF8.GetString(header.Value)));

        using var bodyless = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        using var _ = await transport.SendAsync(bodyless, default);
        Assert.Null(client.Request!.Body);
        Assert.Equal("/", client.Request.PathWithQuery);
    }

    [Fact]
    public async Task BuildsTheResponseWithContentHeadersOnTheContent()
    {
        var client = new RecordingClient
        {
            Response = new WasiHttpResponse(
                201,
                [new("Content-Type", "application/json"), new("ETag", "\"v1\""), new("Content-Length", "2")],
                new MemoryStream("{}"u8.ToArray())),
        };
        var transport = new WasiHttpTransport(client, new WasiHttpFailureMapper());

        using var response = await transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), default);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(2, response.Content.Headers.ContentLength);
        Assert.Equal("\"v1\"", Assert.Single(response.Headers.GetValues("ETag")));
        Assert.Equal("{}", await response.Content.ReadAsStringAsync());
    }

    // An error code surfaces as the HttpRequestException .NET throws for the same failure, with
    // the host's message when it gave one.
    [Theory]
    [InlineData((int)WasiHttpFailureKind.DnsError, HttpRequestError.NameResolutionError)]
    [InlineData((int)WasiHttpFailureKind.TlsCertificateError, HttpRequestError.SecureConnectionError)]
    [InlineData((int)WasiHttpFailureKind.HttpResponseIncomplete, HttpRequestError.InvalidResponse)]
    [InlineData((int)WasiHttpFailureKind.ConnectionRefused, HttpRequestError.ConnectionError)]
    [InlineData((int)WasiHttpFailureKind.ConfigurationError, HttpRequestError.Unknown)]
    [InlineData((int)WasiHttpFailureKind.InternalError, HttpRequestError.Unknown)]
    public async Task ReportsWasiHttpErrorsAsHttpRequestExceptions(int failureKind, HttpRequestError expected)
    {
        var kind = (WasiHttpFailureKind)failureKind;
        var client = new RecordingClient { Failure = new WasiHttpFailureFacts(kind) };
        var transport = new WasiHttpTransport(client, new WasiHttpFailureMapper());

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), default));

        Assert.Equal(expected, failure.HttpRequestError);
        Assert.Equal($"WASI HTTP request failed: {kind}.", failure.Message);
        Assert.Equal("host detail", new WasiHttpFailureMapper()
            .Translate(new WasiHttpFailureFacts(kind, "host detail")).Message);
    }

    // .NET's handlers keep a received header as sent: a value its parser would reject, such as an
    // ETag a CDN sends unquoted, is still readable, and nothing throws.
    [Fact]
    public async Task KeepsReceivedHeaderValuesAsSent()
    {
        var client = new RecordingClient
        {
            Response = new WasiHttpResponse(
                200,
                [
                    new("ETag", "0x8DD2F82FA585D1E"),
                    new("Content-Length", "not a number"),
                    new("Set-Cookie", "a=1"),
                    new("Set-Cookie", "b=2"),
                ],
                new MemoryStream("ok"u8.ToArray())),
        };
        var transport = new WasiHttpTransport(client, new WasiHttpFailureMapper());

        using var response = await transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), default);

        Assert.Equal("0x8DD2F82FA585D1E", Assert.Single(response.Headers.GetValues("ETag")));
        Assert.Equal("not a number", response.Content.Headers.NonValidated["Content-Length"].ToString());
        Assert.False(response.Headers.Contains("Content-Length"));
        Assert.Equal(["a=1", "b=2"], response.Headers.GetValues("Set-Cookie"));
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    // A response .NET cannot represent is disposed, with its body.
    [Fact]
    public async Task DisposesAResponseItCannotBuild()
    {
        var body = new TrackingStream();
        var client = new RecordingClient
        {
            Response = new WasiHttpResponse(1000, [], body),
        };
        var transport = new WasiHttpTransport(client, new WasiHttpFailureMapper());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"), default));
        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task RejectsRequestsWithoutAnAbsoluteUriAndMissingCollaborators()
    {
        var transport = new WasiHttpTransport(new RecordingClient(), new WasiHttpFailureMapper());

        await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, (Uri?)null), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("/relative", UriKind.Relative)), default));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpTransport(null!, new WasiHttpFailureMapper()));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpTransport(new RecordingClient(), null!));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpRequest(null!, "http", "a", "/", [], null));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpRequest("GET", null!, "a", "/", [], null));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpRequest("GET", "http", null!, "/", [], null));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpRequest("GET", "http", "a", null!, [], null));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpRequest("GET", "http", "a", "/", null!, null));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpResponse(200, null!, Stream.Null));
        Assert.Throws<ArgumentNullException>(() => new WasiHttpResponse(200, [], null!));
        var failure = new WasiHttpTransportFailureException(new WasiHttpFailureFacts(WasiHttpFailureKind.LoopDetected, "loop"));
        Assert.Equal(WasiHttpFailureKind.LoopDetected, failure.Facts.Kind);
        Assert.Equal("loop", failure.Message);
    }

    private sealed class RecordingClient : IWasiHttpClient
    {
        public WasiHttpRequest? Request { get; private set; }
        public WasiHttpResponse? Response { get; init; }
        public WasiHttpFailureFacts? Failure { get; init; }

        public Task<WasiHttpResponse> SendAsync(WasiHttpRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            if (Failure is { } failure)
            {
                throw new WasiHttpTransportFailureException(failure);
            }

            return Task.FromResult(Response ?? new WasiHttpResponse(200, [], new MemoryStream()));
        }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
