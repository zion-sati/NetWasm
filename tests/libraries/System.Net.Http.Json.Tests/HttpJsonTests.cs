// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Http.Json.Tests;

public sealed partial class HttpJsonTests
{
#if NETWASM
    [Test]
    public async Task JsonContentRejectsUnsupportedOutboundCharset()
    {
        bool rejected = false;
        try
        {
            _ = JsonContent.Create(new Payload(1, "Ada"), JsonTestContext.Default.Payload,
                new global::System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-16" });
        }
        catch (NotSupportedException)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();

        using JsonContent mutable = JsonContent.Create(
            new Payload(2, "Grace"), JsonTestContext.Default.Payload);
        mutable.Headers.ContentType!.CharSet = "utf-16";
        rejected = false;
        try
        {
            using var destination = new MemoryStream();
            await mutable.CopyToAsync(destination);
        }
        catch (NotSupportedException)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
    }
#endif
    [Test]
    public async Task GetFromJsonAsyncUsesGeneratedMetadata()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"Id\":7,\"Name\":\"Ada\"}", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);

        var result = await client.GetFromJsonAsync(
            new Uri("https://example.test/payload"),
            JsonTestContext.Default.Payload);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(7);
        await Assert.That(result.Name).IsEqualTo("Ada");
        await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Get);
    }

    [Test]
    public async Task NullRequestUriUsesClientBaseAddress()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"Id\":8,\"Name\":\"Base\"}", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/api"),
        };

        var result = await client.GetFromJsonAsync(
            (Uri?)null,
            JsonTestContext.Default.Payload);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(8);
        await Assert.That(handler.LastRequest!.RequestUri).IsEqualTo(client.BaseAddress);
    }

    [Test]
    public async Task PostAndPutAsJsonAsyncUseGeneratedMetadata()
    {
        var requests = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        });
        using var client = new HttpClient(requests);
        var payload = new Payload(9, "Grace");

        using (var post = await client.PostAsJsonAsync(
                   new Uri("https://example.test/post"),
                   payload,
                   JsonTestContext.Default.Payload))
        {
            await Assert.That(post.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(requests.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(await requests.LastRequest.Content!.ReadAsStringAsync())
                .IsEqualTo("{\"Id\":9,\"Name\":\"Grace\"}");
            await Assert.That(requests.LastRequest.Content.Headers.ContentType!.MediaType)
                .IsEqualTo("application/json");
            await Assert.That(requests.LastRequest.Content.Headers.ContentType.CharSet)
                .IsEqualTo("utf-8");
        }

        using (var put = await client.PutAsJsonAsync(
                   "https://example.test/put",
                   payload,
                   JsonTestContext.Default.Payload))
        {
            await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(requests.LastRequest!.Method).IsEqualTo(HttpMethod.Put);
        }
    }

    [Test]
    public async Task ContentJsonReadAndResponseHeadersReadRemainStreaming()
    {
        var body = new CountingReadStream(Encoding.UTF8.GetBytes("{\"Id\":3,\"Name\":\"Lin\"}"));
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body),
        });
        using var client = new HttpClient(handler);
        using var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://example.test/stream")),
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);

        using var raw = await response.Content!.ReadAsStreamAsync();
        await Assert.That(body.ReadCount).IsEqualTo(0);
        raw.Dispose();

        var secondBody = new CountingReadStream(Encoding.UTF8.GetBytes("{\"Id\":4,\"Name\":\"Lin\"}"));
        using var content = new StreamContent(secondBody);
        var value = await content.ReadFromJsonAsync(JsonTestContext.Default.Payload);
        await Assert.That(value!.Id).IsEqualTo(4);
        await Assert.That(secondBody.ReadCount).IsNotEqualTo(0);
    }

    [Test]
    public async Task ReadFromJsonAsyncRejectsUnsupportedCharsetAndStatus()
    {
#if NETWASM
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        content.Headers.ContentType = new global::System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "iso-8859-1",
        };
        await Assert.That(await ThrowsNotSupportedAsync(
            () => content.ReadFromJsonAsync(JsonTestContext.Default.Payload))).IsTrue();
#endif

        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);
        await Assert.That(await ThrowsRequestAsync(
            () => client.GetFromJsonAsync(
                new Uri("https://example.test/failure"),
                JsonTestContext.Default.Payload))).IsTrue();
    }

    private static async Task<bool> ThrowsNotSupportedAsync(Func<Task<Payload?>> operation)
    {
        try
        {
            await operation();
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsRequestAsync(Func<Task<Payload?>> operation)
    {
        try
        {
            await operation();
            return false;
        }
        catch (HttpRequestException)
        {
            return true;
        }
    }

    private sealed class RecordingHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responseFactory());
        }
    }

    private sealed class CountingReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public int ReadCount { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return _inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed record Payload(int Id, string Name);

    [JsonSerializable(typeof(Payload))]
    private sealed partial class JsonTestContext : JsonSerializerContext;
}
