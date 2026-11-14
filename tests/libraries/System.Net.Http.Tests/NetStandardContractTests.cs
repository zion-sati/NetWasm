using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Http.Tests;

// The netstandard 2.1 shapes of the upstream System.Net.Http port: typed header views over one store, managed
// content serialization, the handler pipeline and the client's managed policy.
public sealed class NetStandardContractTests
{
    [Test]
    public async Task TypedHeaderViewsShareTheRawStore()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/"));
        request.Headers.Accept.ParseAdd("text/html;q=0.8");
        request.Headers.Add("Accept", "application/json");

        await Assert.That(request.Headers.Accept.Count).IsEqualTo(2);
        await Assert.That(First(request.Headers.Accept).MediaType).IsEqualTo("text/html");
        await Assert.That(First(request.Headers.Accept).Quality).IsEqualTo(0.8);
        await Assert.That(string.Join("|", request.Headers.GetValues("accept")))
            .IsEqualTo("text/html; q=0.8|application/json");

        // A value added without validation stays visible as a string but not through the typed property.
        request.Headers.TryAddWithoutValidation("Date", "not a date");
        await Assert.That(request.Headers.Date).IsNull();
        await Assert.That(First(request.Headers.GetValues("Date"))).IsEqualTo("not a date");

        await Assert.That(ThrowsInvalidOperation(() => request.Headers.GetValues("X-Missing"))).IsTrue();
        await Assert.That(ThrowsInvalidOperation(() => request.Headers.Add("Content-Type", "text/plain"))).IsTrue();

        request.Headers.ConnectionClose = true;
        await Assert.That(request.Headers.Connection.Contains("close")).IsTrue();
        request.Headers.ConnectionClose = null;
        await Assert.That(request.Headers.ConnectionClose).IsNull();
    }

    [Test]
    public async Task ContentLengthIsComputedUntilExplicitlySet()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        await Assert.That(content.Headers.ContentLength).IsEqualTo(3L);

        content.Headers.ContentLength = null;
        await Assert.That(content.Headers.ContentLength).IsNull();
    }

    [Test]
    public async Task MultipartContentFramesPartsInOrder()
    {
        using var content = new MultipartContent("mixed", "BOUNDARY")
        {
            new StringContent("a"),
            new ByteArrayContent(new byte[] { (byte)'b' }),
        };

        await Assert.That(await content.ReadAsStringAsync()).IsEqualTo(
            "--BOUNDARY\r\nContent-Type: text/plain; charset=utf-8\r\n\r\na" +
            "\r\n--BOUNDARY\r\n\r\nb" +
            "\r\n--BOUNDARY--\r\n");
        await Assert.That(content.Headers.ContentType!.MediaType).IsEqualTo("multipart/mixed");
    }

    [Test]
    public async Task ReadAsStringHonorsTheDeclaredCharset()
    {
        using var content = new ByteArrayContent(Encoding.Unicode.GetBytes("hé"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-16" };

        await Assert.That(await content.ReadAsStringAsync()).IsEqualTo("hé");
    }

    [Test]
    public async Task StreamContentRewindsSeekableSources()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("xxabc"));
        source.Position = 2;
        using var content = new StreamContent(source);

        using var first = new MemoryStream();
        await content.CopyToAsync(first);
        using var second = new MemoryStream();
        await content.CopyToAsync(second);

        await Assert.That(Encoding.UTF8.GetString(first.ToArray())).IsEqualTo("abc");
        await Assert.That(Encoding.UTF8.GetString(second.ToArray())).IsEqualTo("abc");
    }

    [Test]
    public async Task MessageProcessingHandlerProcessesAroundTheInnerHandler()
    {
        var inner = new HeaderRecordingHandler();
        var processing = new ProcessingHandler(inner);
        using var invoker = new HttpMessageInvoker(processing);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/"));

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(string.Join("|", processing.Calls)).IsEqualTo("request|response");
        await Assert.That(inner.ProcessedHeader).IsEqualTo("1");
        await Assert.That(response.ReasonPhrase).IsEqualTo("processed");
    }

    [Test]
    public async Task DelegatingHandlerInnerHandlerIsReplaceableUntilFirstSend()
    {
        var handler = new PassThroughHandler(new HeaderRecordingHandler());
        var replacement = new HeaderRecordingHandler();
        handler.InnerHandler = replacement;
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/"));

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(replacement.CallCount).IsEqualTo(1);
        await Assert.That(ThrowsInvalidOperation(() => handler.InnerHandler = new HeaderRecordingHandler())).IsTrue();
    }

    [Test]
    public async Task HttpMethodIsAnExtensibleValueType()
    {
        var custom = new CustomMethod();

        await Assert.That(custom.Method).IsEqualTo("CUSTOM");
        await Assert.That(HttpMethod.Patch.Method).IsEqualTo("PATCH");
        await Assert.That(new HttpMethod("patch") == HttpMethod.Patch).IsTrue();
        await Assert.That(((HttpMethod?)null) == null).IsTrue();
    }

    [Test]
    public async Task ClientPolicyDefaultsAndPendingRequestCancellation()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);

        await Assert.That(client.Timeout).IsEqualTo(TimeSpan.FromSeconds(100));
        await Assert.That(ThrowsArgument(() => client.BaseAddress = new Uri("relative", UriKind.Relative))).IsTrue();

        using var blocked = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/block"));
        Task<HttpResponseMessage> pending = client.SendAsync(blocked);
        client.CancelPendingRequests();
        await Assert.That(await ThrowsTaskCanceledAsync(() => pending)).IsTrue();

        // The cancellation source is replaced, so later requests still run.
        using var response = await client.GetAsync(new Uri("https://example.test/ok"));
        await Assert.That(response.IsSuccessStatusCode).IsTrue();

        using var resent = new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/ok"));
        using (await client.SendAsync(resent))
        {
        }
        await Assert.That(await ThrowsInvalidOperationAsync(() => client.SendAsync(resent))).IsTrue();
    }

    [Test]
    public async Task ResponseStatusCodesSpanZeroThroughNineHundredNinetyNine()
    {
        using var lowest = new HttpResponseMessage((HttpStatusCode)0);
        using var highest = new HttpResponseMessage((HttpStatusCode)999);

        await Assert.That((int)lowest.StatusCode).IsEqualTo(0);
        await Assert.That((int)highest.StatusCode).IsEqualTo(999);
        await Assert.That(ThrowsArgument(() => new HttpResponseMessage((HttpStatusCode)1000))).IsTrue();

        using var failed = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("kept") };
        await Assert.That(ThrowsHttpRequest(() => failed.EnsureSuccessStatusCode())).IsTrue();
        await Assert.That(await failed.Content.ReadAsStringAsync()).IsEqualTo("kept");
    }

#if NETWASM
    [Test]
    public async Task ClientHandlerExposesTheWasiCapabilityBoundary()
    {
        using var handler = new HttpClientHandler();

        await Assert.That(handler.SupportsProxy).IsFalse();
        await Assert.That(handler.SupportsRedirectConfiguration).IsTrue();
        await Assert.That(handler.MaxRequestContentBufferSize).IsEqualTo(0L);
        handler.Properties["k"] = 1;
        await Assert.That(handler.Properties["k"]).IsEqualTo(1);

        // Redirect and cookie policy run in managed code, as SocketsHttpHandler's do.
        handler.UseCookies = false;
        handler.AllowAutoRedirect = false;
        await Assert.That(handler.UseCookies).IsFalse();
        await Assert.That(handler.AllowAutoRedirect).IsFalse();

        // The wasi:http host owns routes, so proxy settings bind and throw rather than being silently ignored.
        await Assert.That(ThrowsPlatformNotSupported(() => _ = handler.Proxy)).IsTrue();
    }

    private static bool ThrowsPlatformNotSupported(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }
    }
#endif

    // The test project does not reference System.Linq.
    private static T First<T>(IEnumerable<T> values)
    {
        foreach (T value in values)
        {
            return value;
        }

        throw new InvalidOperationException("The sequence is empty.");
    }

    private static bool ThrowsArgument(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool ThrowsInvalidOperation(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool ThrowsHttpRequest(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (HttpRequestException)
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsTaskCanceledAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return false;
        }
        catch (TaskCanceledException)
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsInvalidOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private sealed class CustomMethod() : HttpMethod("CUSTOM");

    private sealed class HeaderRecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? ProcessedHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ProcessedHeader = request.Headers.TryGetValues("X-Processed", out IEnumerable<string>? values)
                ? First(values)
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ProcessingHandler(HttpMessageHandler innerHandler) : MessageProcessingHandler(innerHandler)
    {
        public List<string> Calls { get; } = new();

        protected override HttpRequestMessage ProcessRequest(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add("request");
            request.Headers.Add("X-Processed", "1");
            return request;
        }

        protected override HttpResponseMessage ProcessResponse(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            Calls.Add("response");
            response.ReasonPhrase = "processed";
            return response;
        }
    }

    private sealed class PassThroughHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler);

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/block")
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
