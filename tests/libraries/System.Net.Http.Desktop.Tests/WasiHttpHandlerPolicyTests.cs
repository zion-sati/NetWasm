using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace NetWasm.SystemNetHttp.Tests;

// The policy controls WasiHttpHandler implements as SocketsHttpHandler does, over a scripted transport: redirects,
// cookies, Basic credentials, decompression and the response header limit. Proxies, connection limits and TLS belong
// to the wasi:http host and still throw.
public sealed class WasiHttpHandlerPolicyTests
{
    [Fact]
    public async Task FollowsRedirectsAsSocketsHttpHandlerDoes()
    {
        var transport = new ScriptedTransport(
            request => Redirect(HttpStatusCode.Found, "/second"),
            request => Redirect(HttpStatusCode.SeeOther, "https://other.test/third"),
            request => Text(HttpStatusCode.OK, "done"));
        using var client = new HttpClient(new HttpClientHandler(transport));

        using var response = await client.PostAsync("https://example.test/first", new StringContent("body"));

        Assert.Equal("done", await response.Content.ReadAsStringAsync());
        Assert.Equal(new Uri("https://other.test/third"), response.RequestMessage!.RequestUri);
        Assert.Equal(["POST https://example.test/first", "GET https://example.test/second", "GET https://other.test/third"],
            transport.Requests.Select(request => request.Line));
    }

    [Fact]
    public async Task StopsAtTheRedirectLimitOrWhenRedirectsAreOff()
    {
        var limited = new ScriptedTransport(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => Redirect(HttpStatusCode.Redirect, "/again"), 3).ToArray());
        using (var client = new HttpClient(new HttpClientHandler(limited) { MaxAutomaticRedirections = 2 }))
        using (var response = await client.GetAsync("https://example.test/"))
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(3, limited.Requests.Count);
        }

        var off = new ScriptedTransport(_ => Redirect(HttpStatusCode.MovedPermanently, "/elsewhere"));
        using (var client = new HttpClient(new HttpClientHandler(off) { AllowAutoRedirect = false }))
        using (var response = await client.GetAsync("https://example.test/"))
        {
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Single(off.Requests);
        }
    }

    [Fact]
    public async Task StoresReceivedCookiesAndSendsThemWithTheRequestsOwn()
    {
        var transport = new ScriptedTransport(
            _ => WithHeader(Text(HttpStatusCode.OK, "set"), "Set-Cookie", "session=abc; Path=/"),
            _ => Text(HttpStatusCode.OK, "used"),
            _ => Text(HttpStatusCode.OK, "elsewhere"));
        var handler = new HttpClientHandler(transport);
        using var client = new HttpClient(handler);

        (await client.GetAsync("https://example.test/login")).Dispose();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/data");
        request.Headers.Add("Cookie", "mine=1");
        (await client.SendAsync(request)).Dispose();
        (await client.GetAsync("https://other.test/")).Dispose();

        Assert.Equal("abc", handler.CookieContainer.GetCookies(new Uri("https://example.test/"))["session"]!.Value);
        Assert.Equal("mine=1; session=abc", transport.Requests[1].Cookie);
        Assert.Equal(["mine=1"], request.Headers.GetValues("Cookie"));
        Assert.Null(transport.Requests[2].Cookie);
    }

    [Fact]
    public async Task CookiesAreLeftAloneWhenUseCookiesIsOff()
    {
        var transport = new ScriptedTransport(
            _ => WithHeader(Text(HttpStatusCode.OK, "set"), "Set-Cookie", "session=abc"),
            _ => Text(HttpStatusCode.OK, "next"));
        var handler = new HttpClientHandler(transport) { UseCookies = false };
        using var client = new HttpClient(handler);

        (await client.GetAsync("https://example.test/")).Dispose();
        (await client.GetAsync("https://example.test/")).Dispose();

        Assert.Null(transport.Requests[1].Cookie);
        Assert.Empty(handler.CookieContainer.GetCookies(new Uri("https://example.test/")));
    }

    [Fact]
    public async Task AnswersABasicChallengeAndPreAuthenticatesAfterward()
    {
        var transport = new ScriptedTransport(
            _ => Challenge("Basic realm=\"test\""),
            _ => Text(HttpStatusCode.OK, "first"),
            _ => Text(HttpStatusCode.OK, "second"));
        var credentials = new NetworkCredential("user", "secret");
        using var client = new HttpClient(new HttpClientHandler(transport)
        {
            Credentials = credentials,
            PreAuthenticate = true,
        });

        (await client.GetAsync("https://example.test/a")).Dispose();
        (await client.GetAsync("https://example.test/b")).Dispose();

        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:secret"));
        Assert.Null(transport.Requests[0].Authorization);
        Assert.Equal(expected, transport.Requests[1].Authorization);
        Assert.Equal(expected, transport.Requests[2].Authorization);
        Assert.Equal(3, transport.Requests.Count);
    }

    [Fact]
    public async Task ReturnsTheChallengeWithoutCredentials()
    {
        var transport = new ScriptedTransport(_ => Challenge("Basic realm=\"test\""));
        using var client = new HttpClient(new HttpClientHandler(transport));

        using var response = await client.GetAsync("https://example.test/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    public async Task DecompressesWhatItAsksFor(string encoding)
    {
        var transport = new ScriptedTransport(_ => Compressed(encoding, "hello, compressed"));
        using var client = new HttpClient(new HttpClientHandler(transport)
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });

        using var response = await client.GetAsync("https://example.test/");

        Assert.Equal("hello, compressed", await response.Content.ReadAsStringAsync());
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(["gzip", "deflate"], transport.Requests[0].AcceptEncoding);
    }

    [Fact]
    public async Task NeverAsksForZstandard()
    {
        var transport = new ScriptedTransport(_ => Text(HttpStatusCode.OK, "plain"));
        using var client = new HttpClient(new HttpClientHandler(transport)
        {
            AutomaticDecompression = DecompressionMethods.All,
        });

        (await client.GetAsync("https://example.test/")).Dispose();

        Assert.Equal(["gzip", "deflate", "br"], transport.Requests[0].AcceptEncoding);
    }

    [Fact]
    public async Task RejectsResponseHeadersPastTheLimit()
    {
        var transport = new ScriptedTransport(_ => WithHeader(Text(HttpStatusCode.OK, "big"), "X-Big", new string('x', 2048)));
        using var client = new HttpClient(new HttpClientHandler(transport) { MaxResponseHeadersLength = 1 });

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://example.test/"));

        Assert.Equal(HttpRequestError.ConfigurationLimitExceeded, failure.HttpRequestError);
    }

    [Fact]
    public async Task SettingsHaveSocketsHttpHandlersDefaultsAndValidation()
    {
        var handler = new HttpClientHandler(new ScriptedTransport(_ => Text(HttpStatusCode.OK, "ok")));

        Assert.True(handler.AllowAutoRedirect);
        Assert.Equal(50, handler.MaxAutomaticRedirections);
        Assert.True(handler.UseCookies);
        Assert.NotNull(handler.CookieContainer);
        Assert.False(handler.PreAuthenticate);
        Assert.Null(handler.Credentials);
        Assert.False(handler.UseDefaultCredentials);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(64, handler.MaxResponseHeadersLength);
        Assert.True(handler.SupportsAutomaticDecompression);
        Assert.True(handler.SupportsRedirectConfiguration);
        Assert.False(handler.SupportsProxy);
        Assert.Throws<ArgumentOutOfRangeException>(() => handler.MaxAutomaticRedirections = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => handler.MaxResponseHeadersLength = 0);

        using var client = new HttpClient(handler);
        (await client.GetAsync("https://example.test/")).Dispose();

        Assert.Throws<InvalidOperationException>(() => handler.AllowAutoRedirect = false);
        Assert.Throws<InvalidOperationException>(() => handler.CookieContainer = new CookieContainer());
    }

    [Fact]
    public void HostOwnedControlsStillThrow()
    {
        var handler = new HttpClientHandler(new ScriptedTransport());

        Assert.Throws<PlatformNotSupportedException>(() => handler.UseProxy);
        Assert.Throws<PlatformNotSupportedException>(() => handler.Proxy = null);
        Assert.Throws<PlatformNotSupportedException>(() => handler.DefaultProxyCredentials);
        Assert.Throws<PlatformNotSupportedException>(() => handler.MaxConnectionsPerServer = 2);
        Assert.Throws<PlatformNotSupportedException>(() => handler.SslProtocols);
        Assert.Throws<PlatformNotSupportedException>(() => handler.ServerCertificateCustomValidationCallback = null);
        Assert.Throws<PlatformNotSupportedException>(() => handler.CheckCertificateRevocationList);
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location)
    {
        var response = Text(status, "moved");
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Challenge(string challenge)
    {
        var response = Text(HttpStatusCode.Unauthorized, "who?");
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", challenge);
        return response;
    }

    private static HttpResponseMessage WithHeader(HttpResponseMessage response, string name, string value)
    {
        response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    private static HttpResponseMessage Compressed(string encoding, string text)
    {
        var buffer = new MemoryStream();
        using (Stream compressor = encoding == "gzip"
            ? new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true)
            : new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(Encoding.UTF8.GetBytes(text));
        }
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
        response.Content.Headers.ContentEncoding.Add(encoding);
        return response;
    }

    // Answers each send with the next scripted response and records what the request carried as it was sent.
    private sealed class ScriptedTransport(params Func<HttpRequestMessage, HttpResponseMessage>[] script) : IHttpTransport
    {
        public List<SentRequest> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                $"{request.Method} {request.RequestUri}",
                request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null,
                request.Headers.Authorization?.ToString(),
                request.Headers.AcceptEncoding.Select(value => value.Value).ToArray()));
            return Task.FromResult(script[Requests.Count - 1](request));
        }
    }

    private sealed record SentRequest(string Line, string? Cookie, string? Authorization, string[] AcceptEncoding);
}
