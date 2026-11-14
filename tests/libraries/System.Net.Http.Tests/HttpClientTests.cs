using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Http.Tests;

public sealed class HttpClientTests
{
    [Test]
    public async Task SendAsyncPreservesRequestFactsAndResponseContent()
    {
        var handler = new RecordingHandler(201, "created");
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("https://example.test/echo"))
        {
            Content = new StringContent("hello", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-Request-Id", "http-row");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);

        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(ReferenceEquals(handler.LastRequest, request)).IsTrue();
        await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.LastRequest.RequestUri!.ToString())
            .IsEqualTo("https://example.test/echo");
        await Assert.That(string.Join("|", handler.LastRequest.Headers.GetValues("x-request-id")))
            .IsEqualTo("http-row");
        await Assert.That(await handler.LastRequest.Content!.ReadAsStringAsync())
            .IsEqualTo("hello");
        await Assert.That((int)response.StatusCode).IsEqualTo(201);
        await Assert.That(await response.Content!.ReadAsStringAsync()).IsEqualTo("created");
    }

    [Test]
    public async Task ConvenienceMethodsSelectGetAndPost()
    {
        var handler = new RecordingHandler(200, "ok");
        using var client = new HttpClient(handler);

        using (var getResponse = await client.GetAsync(new Uri("https://example.test/get")))
        {
            await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Get);
            await Assert.That(getResponse.IsSuccessStatusCode).IsTrue();
        }

        using (var postResponse = await client.PostAsync(
                   new Uri("https://example.test/post"),
                   new StringContent("body")))
        {
            await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(await handler.LastRequest.Content!.ReadAsStringAsync())
                .IsEqualTo("body");
            await Assert.That(postResponse.IsSuccessStatusCode).IsTrue();
        }

        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task NullRequestUriUsesBaseAddress()
    {
        var handler = new RecordingHandler(200, "ok");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/base"),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, client.BaseAddress);
        request.RequestUri = null!;

        using var response = await client.SendAsync(request);

        await Assert.That(handler.LastRequest!.RequestUri).IsEqualTo(client.BaseAddress);
    }

    [Test]
    public async Task DelegatingHandlerForwardsAndDisposesInnerHandler()
    {
        var inner = new CountingHandler();
        using var handler = new PassThroughHandler(inner);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        using var request = NewRequest();

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(inner.CallCount).IsEqualTo(1);
        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(inner.DisposeCount).IsEqualTo(0);

        invoker.Dispose();
        await Assert.That(inner.DisposeCount).IsEqualTo(1);
        await Assert.That(await ThrowsObjectDisposedAsync(
            () => handler.SendForTestAsync(NewRequest()))).IsTrue();
    }

    [Test]
    public async Task FormContentAndHeadersPreserveCommonManagedSemantics()
    {
        using var content = new FormUrlEncodedContent(
            new[] { new KeyValuePair<string, string>("a b", "c+d") });

        await Assert.That(await content.ReadAsStringAsync()).IsEqualTo("a+b=c%2Bd");
        await Assert.That(content.Headers.ContentType!.MediaType)
            .IsEqualTo("application/x-www-form-urlencoded");

        content.Headers.Add("X-Test", "one");
        content.Headers.Add("x-test", "two");
        await Assert.That(string.Join("|", content.Headers.GetValues("X-TEST")))
            .IsEqualTo("one|two");
        await Assert.That(content.Headers.Contains("x-test")).IsTrue();
        await Assert.That(content.Headers.TryGetValues("X-Test", out var values)).IsTrue();
        await Assert.That(string.Join("|", values!)).IsEqualTo("one|two");
    }

    [Test]
    public async Task StreamContentAndRequestErrorsRemainObservable()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("streamed"));
        using var content = new StreamContent(source);
        using var stream = await content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("streamed");

        var exception = new HttpRequestException("failed");
        await Assert.That(exception.Message).IsEqualTo("failed");
    }

    [Test]
    public async Task RequestOptionsAndMethodsRemainStronglyTyped()
    {
        using var request = NewRequest();
        var key = new HttpRequestOptionsKey<int>("attempt");
        request.Options.Set(key, 2);

        await Assert.That(request.Options.TryGetValue(key, out var attempt)).IsTrue();
        await Assert.That(attempt).IsEqualTo(2);
        await Assert.That(HttpMethod.Get).IsEqualTo(new HttpMethod("get"));
        await Assert.That(HttpMethod.Get).IsNotEqualTo(HttpMethod.Post);
    }

    [Test]
    public async Task RequestsResponsesAndContentHonorValidationAndDisposal()
    {
        await Assert.That(ThrowsArgument(() => new HttpRequestMessage(
            null!,
            new Uri("https://example.test/")))).IsTrue();
        await Assert.That(ThrowsArgument(() => new HttpMethod(string.Empty))).IsTrue();

        using var request = NewRequest();
        request.Content = new StringContent("request");
        request.Dispose();
        await Assert.That(ThrowsObjectDisposed(() => SetRequestContent(request)))
            .IsTrue();

        using var response = CreateResponse(200);
        response.Content = new StringContent("response");
        response.Dispose();
        await Assert.That(ThrowsObjectDisposed(() => SetResponseContent(response)))
            .IsTrue();

        var content = new StringContent("content");
        content.Dispose();
        await Assert.That(ThrowsObjectDisposed(
            () => content.ReadAsStringAsync().GetAwaiter().GetResult()))
            .IsTrue();
    }

    [Test]
    public async Task ClientAndInvokerRejectUseAfterDispose()
    {
        var client = new HttpClient(new CountingHandler());
        client.Dispose();
        await Assert.That(await ThrowsObjectDisposedAsync(
            () => SendAsync(client))).IsTrue();

        using var activeClient = new HttpClient(new CountingHandler());
        await Assert.That(ThrowsArgument(
            () => activeClient.SendAsync((HttpRequestMessage)null!).GetAwaiter().GetResult()))
            .IsTrue();

        var invoker = new HttpMessageInvoker(new CountingHandler());
        invoker.Dispose();
        await Assert.That(await ThrowsObjectDisposedAsync(
            () => SendAsync(invoker))).IsTrue();

        using var activeInvoker = new HttpMessageInvoker(new CountingHandler());
        await Assert.That(ThrowsArgument(
            () => activeInvoker.SendAsync((HttpRequestMessage)null!, CancellationToken.None)
                .GetAwaiter().GetResult()))
            .IsTrue();
    }

    private static HttpRequestMessage NewRequest() =>
        new(HttpMethod.Get, new Uri("https://example.test/"));

    private static HttpResponseMessage CreateResponse(int statusCode)
        => new((HttpStatusCode)statusCode);

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

    private static bool ThrowsObjectDisposed(Action operation)
    {
        try
        {
            operation();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static void SetRequestContent(HttpRequestMessage request) =>
        request.Content = new StringContent("after");

    private static void SetResponseContent(HttpResponseMessage response) =>
        response.Content = new StringContent("after");

    // HttpClient.Send blocks on the platform's HTTP, as on .NET; a port nothing listens on refuses the connection.
    [Test]
    public async Task SendToAPortNothingListensOnFails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var client = new HttpClient();
        HttpRequestError? error = null;
        try
        {
            using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/"));
        }
        catch (HttpRequestException e)
        {
            error = e.HttpRequestError;
        }

        await Assert.That(error).IsEqualTo(HttpRequestError.ConnectionError);
    }

    private static async Task SendAsync(HttpClient client)
    {
        using var request = NewRequest();
        using var response = await client.SendAsync(request);
    }

    private static async Task SendAsync(HttpMessageInvoker invoker)
    {
        using var request = NewRequest();
        using var response = await invoker.SendAsync(request, CancellationToken.None);
    }

    private static async Task<bool> ThrowsObjectDisposedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private sealed class RecordingHandler(int statusCode, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            var response = CreateResponse(statusCode);
            response.Content = new StringContent(body);
            return Task.FromResult(response);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public int DisposeCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(CreateResponse(204));
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

    private sealed class PassThroughHandler(HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        public Task<HttpResponseMessage> SendForTestAsync(HttpRequestMessage request) =>
            base.SendAsync(request, CancellationToken.None);
    }
}
