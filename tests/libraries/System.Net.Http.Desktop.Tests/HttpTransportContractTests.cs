using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace NetWasm.SystemNetHttp.Tests;

public sealed class HttpTransportContractTests
{
    [Fact]
    public async Task ClientSendsTheRequestThroughTheNeutralTransport()
    {
        var transport = new RecordingTransport();
        using var client = new HttpClient(new HttpClientHandler(transport));
        using var cancellation = new CancellationTokenSource();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("https://example.test/echo"))
        {
            Content = new StringContent("hello", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Request-Id", "h2");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);

        Assert.Equal(1, transport.CallCount);
        Assert.Same(request, transport.LastRequest);
        // HttpClient links the caller's token with its pending-request and timeout sources.
        Assert.True(transport.LastCancellationToken.CanBeCanceled);
        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
        Assert.Equal("https://example.test/echo", transport.LastRequest.RequestUri!.ToString());
        Assert.Equal("h2", transport.LastRequest.Headers.GetValues("x-request-id").Single());
        Assert.Equal("hello", await transport.LastRequest.Content!.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("created", await response.Content!.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConvenienceMethodsSelectTheExpectedManagedMethods()
    {
        var transport = new RecordingTransport();
        using var client = new HttpClient(new HttpClientHandler(transport));

        using (var getResponse = await client.GetAsync(new Uri("https://example.test/get")))
        {
            Assert.Equal(HttpMethod.Get, transport.LastRequest!.Method);
            Assert.Equal(HttpStatusCode.Created, getResponse.StatusCode);
        }

        using (var postResponse = await client.PostAsync(
                   new Uri("https://example.test/post"),
                   new StringContent("body")))
        {
            Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
            Assert.Equal("body", await transport.LastRequest.Content!.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        }
    }

    [Fact]
    public async Task DelegatingHandlerForwardsExactlyOnceAndDisposesInnerHandler()
    {
        var inner = new CountingHandler();
        using var handler = new PassThroughHandler(inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = NewRequest();

        using var response = await invoker.SendAsync(request, default);

        Assert.Equal(1, inner.CallCount);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Throws<InvalidOperationException>(() => handler.InnerHandler = new CountingHandler());
    }

    [Fact]
    public async Task ContentAndHeaderAdaptationsPreserveCommonManagedSemantics()
    {
        using var content = new FormUrlEncodedContent(
            new[] { new KeyValuePair<string, string>("a b", "c+d") });

        Assert.Equal("a+b=c%2Bd", await content.ReadAsStringAsync());
        Assert.Equal("application/x-www-form-urlencoded", content.Headers.ContentType!.MediaType);

        content.Headers.Add("X-Test", "one");
        content.Headers.Add("x-test", "two");
        Assert.Equal(new[] { "one", "two" }, content.Headers.GetValues("X-TEST"));
        Assert.True(content.Headers.Contains("x-test"));
        Assert.True(content.Headers.TryGetValues("X-Test", out var values));
        Assert.Equal(new[] { "one", "two" }, values);
    }

    [Fact]
    public async Task StreamContentAndReadAsStreamPreserveTheManagedContentSurface()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("streamed"));
        using var content = new StreamContent(source);

        using var stream = await content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal("streamed", await reader.ReadToEndAsync());

        var exception = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "failed",
            inner: null,
            HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(HttpRequestError.ConnectionError, exception.HttpRequestError);
    }

    [Fact]
    public void OptionsAndMethodValuesRemainStronglyTyped()
    {
        var request = NewRequest();
        var key = new HttpRequestOptionsKey<int>("attempt");
        request.Options.Set(key, 2);

        Assert.True(request.Options.TryGetValue(key, out var attempt));
        Assert.Equal(2, attempt);
        Assert.Equal(HttpMethod.Get, new HttpMethod("get"));
        Assert.NotEqual(HttpMethod.Get, HttpMethod.Post);
    }

    [Fact]
    public void RequestAndResponseValidateAndOwnContent()
    {
        // Relative request URIs are resolved against HttpClient.BaseAddress; status codes span 0 through 999.
        using (var relative = new HttpRequestMessage(HttpMethod.Get, new Uri("relative", UriKind.Relative)))
        {
            Assert.False(relative.RequestUri!.IsAbsoluteUri);
        }
        using (var lowest = new HttpResponseMessage((HttpStatusCode)0))
        {
            Assert.Equal(0, (int)lowest.StatusCode);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HttpResponseMessage((HttpStatusCode)1000));
        Assert.Throws<ArgumentException>(() => new HttpMethod(string.Empty));

        var requestContent = new TrackingContent();
        var request = NewRequest();
        request.Content = requestContent;
        request.Dispose();
        Assert.Equal(1, requestContent.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => request.Content = new EmptyContent());

        var responseContent = new TrackingContent();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent };
        Assert.True(response.IsSuccessStatusCode);
        response.Dispose();
        Assert.Equal(1, responseContent.DisposeCount);
    }

    [Fact]
    public async Task ClientAndInvokerRejectUseAfterDispose()
    {
        var transport = new RecordingTransport();
        var client = new HttpClient(new HttpClientHandler(transport));
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendAsync(NewRequest()));

        var invoker = new HttpMessageInvoker(new CountingHandler());
        invoker.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => invoker.SendAsync(NewRequest(), default));
    }

    private static HttpRequestMessage NewRequest() =>
        new(HttpMethod.Get, new Uri("https://example.test/"));

    private sealed class RecordingTransport : IHttpTransport
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new HttpResponseMessage(
                CallCount == 1 ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent(CallCount == 1 ? "created" : "ok"),
            });
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        // protected internal: these tests compile the package sources into their own assembly.
        protected internal override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class PassThroughHandler : DelegatingHandler
    {
        public PassThroughHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected internal override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
