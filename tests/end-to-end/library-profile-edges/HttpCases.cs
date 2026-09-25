using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.LibraryProfileEdges;

public sealed record JsonPayload(int Id, string Name);

[JsonSerializable(typeof(JsonPayload))]
internal sealed partial class JsonContext : JsonSerializerContext;

public static class HttpCases
{
    public static async Task ShortReads(bool useContext)
    {
        var body = new Body("{\"Id\":7,\"Name\":\"Ω😀\"}");
        using var client = CreateClient(body);
        var value = useContext
            ? (JsonPayload?)await client.GetFromJsonAsync("items", typeof(JsonPayload), JsonContext.Default)
            : await client.GetFromJsonAsync("items", JsonContext.Default.JsonPayload);
        Program.Require(value is { Id: 7, Name: "Ω😀" });
        Program.Require(body.Reads > 20 && body.ReleaseCount == 1);
    }

    public static async Task PendingReadCancellation()
    {
        var body = new Body("{}", pending: true);
        using var cancellation = new CancellationTokenSource();
        using var client = CreateClient(body);
        var task = client.GetFromJsonAsync("items", JsonContext.Default.JsonPayload, cancellation.Token);
        Program.Require(body.ReadStarted && !task.IsCompleted && body.ReleaseCount == 0);
        cancellation.Cancel();
        try
        {
            await task;
            throw new InvalidOperationException("Cancellation was ignored.");
        }
        catch (OperationCanceledException exception)
        {
            Program.Require(exception.CancellationToken == cancellation.Token && task.IsCanceled);
        }
        Program.Require(body.ReleaseCount == 1);
    }

    public static async Task MalformedBody()
    {
        var body = new Body("{\"Id\":7,\"Name\":");
        using var client = CreateClient(body);
        var task = client.GetFromJsonAsync("items", JsonContext.Default.JsonPayload);
        try
        {
            await task;
            throw new InvalidOperationException("Malformed JSON was accepted.");
        }
        catch (JsonException) { }
        Program.Require(task.IsFaulted && body.ReleaseCount == 1);
    }

    public static async Task MetadataWrites()
    {
        var methods = new List<HttpMethod>();
        using var handler = new ResponseHandler(async (request, cancellation) =>
        {
            Program.Require(request.RequestUri!.AbsoluteUri == "https://example.test/items");
            Program.Require(await request.Content!.ReadAsStringAsync(cancellation) == "{\"Id\":9,\"Name\":\"Ada\"}");
            methods.Add(request.Method);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(-1) };
        var value = new JsonPayload(9, "Ada");
        using var post = await client.PostAsJsonAsync("https://example.test/items", value, JsonContext.Default.JsonPayload);
        using var put = await client.PutAsJsonAsync("https://example.test/items", value, JsonContext.Default.JsonPayload);
        Program.Require(post.StatusCode == HttpStatusCode.NoContent && put.StatusCode == HttpStatusCode.NoContent);
        Program.Require(methods.Count == 2 && methods[0] == HttpMethod.Post && methods[1] == HttpMethod.Put);
    }

    public static async Task ContentContextRead()
    {
        var body = new Body("{\"Id\":7,\"Name\":\"Ω😀\"}");
        using var content = new StreamContent(body);
        var value = (JsonPayload?)await content.ReadFromJsonAsync(typeof(JsonPayload), JsonContext.Default);
        Program.Require(value is { Id: 7, Name: "Ω😀" } && body.Reads > 20 && body.ReleaseCount == 1);
    }

    public static async Task PipelineFailureAndOwnership()
    {
        var events = new List<int>();
        var failure = new IOException("Planned body-independent handler failure.");
        var terminal = new ResponseHandler((_, _) =>
        {
            events.Add(3);
            return Task.FromException<HttpResponseMessage>(failure);
        });
        var inner = new Layer(2, 4, events) { InnerHandler = terminal };
        var outer = new Layer(1, 5, events) { InnerHandler = inner };
        var client = new HttpClient(outer) { Timeout = TimeSpan.FromMilliseconds(-1) };
        try
        {
            await client.GetAsync("https://example.test/items");
            throw new InvalidOperationException("Handler failure was ignored.");
        }
        catch (IOException observed) { Program.Require(ReferenceEquals(failure, observed)); }
        Program.Require(events.Count == 5);
        for (var index = 0; index < events.Count; index++) Program.Require(events[index] == index + 1);
        Program.Require(outer.Disposals == 0 && inner.Disposals == 0 && terminal.Disposals == 0);
        client.Dispose();
        client.Dispose();
        Program.Require(outer.Disposals == 1 && inner.Disposals == 1 && terminal.Disposals == 1);
    }

    private static HttpClient CreateClient(Body body) => new(new ResponseHandler((request, _) =>
    {
        Program.Require(request.RequestUri!.AbsoluteUri == "https://example.test/items");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
    })) { BaseAddress = new Uri("https://example.test/"), Timeout = TimeSpan.FromMilliseconds(-1) };

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Disposals;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }

    private sealed class Layer(int before, int after, List<int> events) : DelegatingHandler
    {
        public int Disposals;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            events.Add(before);
            try { return await base.SendAsync(request, cancellationToken); }
            finally { events.Add(after); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }

    private sealed class Body(string json, bool pending = false) : Stream
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(json);
        private int _position;
        public int Reads;
        public int ReleaseCount;
        public bool ReadStarted;
        public override bool CanRead => ReleaseCount == 0;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (ReleaseCount != 0) throw new ObjectDisposedException(nameof(Body));
            ReadStarted = true;
            Reads++;
            if (buffer.Length == 0 || _position == _bytes.Length) return 0;
            buffer[0] = _bytes[_position++];
            return 1;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return pending ? new ValueTask<int>(WaitForCancellation(cancellationToken)) : new ValueTask<int>(Read(buffer.Span));
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        private async Task<int> WaitForCancellation(CancellationToken cancellationToken)
        {
            ReadStarted = true;
            var completion = new TaskCompletionSource<int>();
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return await completion.Task;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && ReleaseCount == 0) ReleaseCount++;
            base.Dispose(disposing);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
