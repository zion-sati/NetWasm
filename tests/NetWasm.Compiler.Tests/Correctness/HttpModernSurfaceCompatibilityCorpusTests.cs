// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

// System.Net.Http's surface newer than .NET Standard 2.1, as upstream's WASI build has it: synchronous content
// serialization, the version policy, non-validated headers, the HttpRequestException and HttpIOException family, the
// compressed contents and SocketsHttpHandler; and System.Net.Primitives' DecompressionMethods members and IPNetwork.
// SocketsHttpHandler is supported on NetWasm's WASI, over wasi:sockets and wasi:tls, as on desktop. Where the platforms
// differ by design (Zstandard's RFC 9659 windows are not available on WASI), the input checks the platform's own
// contract, keyed on OperatingSystem.IsWasi. GZip and Brotli content, which need the native codecs a corpus module does not link, are in the System.Net.Http
// library tests.
public sealed class HttpModernSurfaceCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ModernHttpSurfaceMatchesDesktopAcrossProfilesAndTargets() =>
        runner.Run(new(
            "HttpModernSurfaceCompatibilityCorpus",
            "NetWasm.Correctness.HttpModernSurface",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            // HttpClient's timeout timer waits on the platform's waitable set.
            RequiresReactor = true,
            ExpectedReturnValues = ImmutableDictionary<int, int>.Empty
                .Add(0, 100).Add(1, 100).Add(3, 100).Add(4, 100).Add(6, 100).Add(7, 100).Add(8, 100).Add(9, 100),
        });

    private const string Source = """
        using System;
        using System.IO;
        using System.IO.Compression;
        using System.Net;
        using System.Net.Http;
        using System.Net.Http.Headers;
        using System.Text;
        using System.Threading;
        using System.Threading.Tasks;

        #pragma warning disable SYSLIB5008 // HttpRequestMessage.ConnectionId is experimental.

        namespace NetWasm.Correctness.HttpModernSurface;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => SynchronousContent(),
                1 => DerivedContent(),
                2 => MultipartHeaderEncoding(),
                3 => Methods(),
                4 => VersionPolicyAndClientDefaults(),
                5 => NonValidatedHeaders(),
                6 => Exceptions(),
                7 => CompressedContent(),
                8 => SocketsHandler(),
                9 => PrimitivesSurface(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int SynchronousContent()
            {
                using var bytes = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
                using (var stream = bytes.ReadAsStream(CancellationToken.None))
                {
                    if (stream.ReadByte() != 1) return 1;
                }
                var copy = new MemoryStream();
                bytes.CopyTo(copy, null, CancellationToken.None);
                if (copy.Length != 4) return 2;

                using var text = new StringContent("café", Encoding.UTF8, new MediaTypeHeaderValue("text/plain", "utf-8"));
                if (text.Headers.ContentType!.ToString() != "text/plain; charset=utf-8") return 3;
                using var json = new StringContent("{}", new MediaTypeHeaderValue("application/json"));
                if (json.Headers.ContentType!.ToString() != "application/json") return 4;
                text.LoadIntoBufferAsync(1024, CancellationToken.None).GetAwaiter().GetResult();
                if (text.ReadAsStream().Length != 5) return 5;

                using var large = new StreamContent(new MemoryStream(new byte[100]));
                try
                {
                    large.LoadIntoBufferAsync(10, CancellationToken.None).GetAwaiter().GetResult();
                    return 6;
                }
                catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
                {
                }

                using var streamed = new StreamContent(new MemoryStream(new byte[] { 5, 6, 7 }));
                var read = new MemoryStream();
                streamed.ReadAsStream().CopyTo(read);
                if (read.Length != 3 || read.ToArray()[2] != 7) return 7;

                using var memory = new ReadOnlyMemoryContent(new byte[] { 8, 9 });
                var memoryCopy = new MemoryStream();
                memory.CopyTo(memoryCopy, null, CancellationToken.None);
                if (memoryCopy.Length != 2 || memory.ReadAsStream().ReadByte() != 8) return 8;

                using var cached = new ByteArrayContent(new byte[] { 1 });
                _ = cached.ReadAsStreamAsync().GetAwaiter().GetResult();
                try
                {
                    cached.ReadAsStream();
                    return 9;
                }
                catch (HttpRequestException)
                {
                }
                return 100;
            }

            private sealed class AsyncOnlyContent : HttpContent
            {
                protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                    stream.WriteAsync(new byte[] { 7, 8 }, 0, 2);

                protected override bool TryComputeLength(out long length)
                {
                    length = 2;
                    return true;
                }
            }

            private sealed class SynchronousOnlyContent : HttpContent
            {
                protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                    throw new InvalidOperationException("asynchronous");

                protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
                    stream.Write(new byte[] { 9, 9, 9 }, 0, 3);

                protected override bool TryComputeLength(out long length)
                {
                    length = -1;
                    return false;
                }
            }

            private sealed class OwnStreamContent : HttpContent
            {
                protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                    Task.CompletedTask;

                protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
                    new MemoryStream(new byte[] { 42 });

                protected override bool TryComputeLength(out long length)
                {
                    length = 1;
                    return true;
                }
            }

            private static int DerivedContent()
            {
                using var asyncOnly = new AsyncOnlyContent();
                try
                {
                    asyncOnly.CopyTo(new MemoryStream(), null, CancellationToken.None);
                    return 1;
                }
                catch (NotSupportedException)
                {
                }
                try
                {
                    asyncOnly.ReadAsStream();
                    return 2;
                }
                catch (NotSupportedException)
                {
                }
                using var asyncAgain = new AsyncOnlyContent();
                if (asyncAgain.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length != 2) return 3;

                using var synchronous = new SynchronousOnlyContent();
                var stream = synchronous.ReadAsStream();
                if (stream.Length != 3 || stream.ReadByte() != 9) return 4;
                var copy = new MemoryStream();
                synchronous.CopyTo(copy, null, CancellationToken.None);
                if (copy.Length != 3) return 5;

                using var own = new OwnStreamContent();
                if (own.ReadAsStream(CancellationToken.None).ReadByte() != 42) return 6;
                return 100;
            }

            private static int MultipartHeaderEncoding()
            {
                using var multipart = new MultipartContent("mixed", "BOUNDARY")
                {
                    HeaderEncodingSelector = (name, content) => name == "X-Name" ? Encoding.UTF8 : null,
                };
                var first = new StringContent("one");
                first.Headers.TryAddWithoutValidation("X-Name", "café");
                multipart.Add(first);
                var second = new ByteArrayContent(new byte[] { 0x41, 0x42 });
                second.Headers.TryAddWithoutValidation("X-Other", "café");
                multipart.Add(second);

                var synchronous = new MemoryStream();
                multipart.CopyTo(synchronous, null, CancellationToken.None);
                var asynchronous = new MemoryStream();
                multipart.CopyToAsync(asynchronous).GetAwaiter().GetResult();
                var read = new MemoryStream();
                multipart.ReadAsStream().CopyTo(read);
                if (!synchronous.ToArray().AsSpan().SequenceEqual(asynchronous.ToArray())) return 1;
                if (!read.ToArray().AsSpan().SequenceEqual(asynchronous.ToArray())) return 2;
                var sum = 0;
                foreach (var b in synchronous.ToArray()) sum = unchecked(sum * 31 + b);
                return (int)(synchronous.Length * 1000) + (sum & 0x3FF);
            }

            private static int Methods()
            {
                if (!ReferenceEquals(HttpMethod.Parse("get"), HttpMethod.Get)) return 1;
                if (!ReferenceEquals(HttpMethod.Parse("QUERY"), HttpMethod.Query)) return 2;
                if (!ReferenceEquals(HttpMethod.Parse("Connect"), HttpMethod.Connect) || HttpMethod.Connect.Method != "CONNECT") return 3;
                var custom = HttpMethod.Parse("PROPFIND".AsSpan());
                if (custom.Method != "PROPFIND" || custom == HttpMethod.Get) return 4;
                try
                {
                    HttpMethod.Parse("BAD METHOD");
                    return 5;
                }
                catch (FormatException)
                {
                }
                return 100;
            }

            private sealed class CapturingHandler : HttpMessageHandler
            {
                public HttpRequestMessage? Last;

                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    Last = request;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("body:" + request.Method) });
                }

                protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    Last = request;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new MemoryStream(Encoding.ASCII.GetBytes("sync"))),
                    };
                }
            }

            private static int VersionPolicyAndClientDefaults()
            {
                var handler = new CapturingHandler();
                using var client = new HttpClient(handler)
                {
                    BaseAddress = new Uri("http://example.test/"),
                    DefaultRequestVersion = HttpVersion.Version20,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                };
                if (client.GetStringAsync("a", CancellationToken.None).GetAwaiter().GetResult() != "body:GET") return 1;
                if (handler.Last!.Version != HttpVersion.Version20 || handler.Last.VersionPolicy != HttpVersionPolicy.RequestVersionOrHigher) return 2;
                if (client.GetByteArrayAsync(new Uri("http://example.test/b"), CancellationToken.None).GetAwaiter().GetResult().Length != 8) return 3;
                using (var stream = client.GetStreamAsync("c", CancellationToken.None).GetAwaiter().GetResult())
                {
                    if (stream.ReadByte() != 'b') return 4;
                }
                try
                {
                    client.DefaultRequestVersion = HttpVersion.Version11;
                    return 5;
                }
                catch (InvalidOperationException)
                {
                }
                try
                {
                    client.DefaultVersionPolicy = (HttpVersionPolicy)7;
                    return 6;
                }
                catch (ArgumentException)
                {
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.test/d");
                if (request.VersionPolicy != HttpVersionPolicy.RequestVersionOrLower) return 7;
                try
                {
                    request.VersionPolicy = (HttpVersionPolicy)7;
                    return 8;
                }
                catch (ArgumentException)
                {
                }
                if (request.ConnectionId is not null) return 9;
                request.ConnectionId = 5;
                if (request.ConnectionId != 5) return 10;
                request.ConnectionId = null;
                if (request.ConnectionId is not null) return 11;

                using var response = client.Send(request);
                if (response.Content.ReadAsStream().ReadByte() != 's') return 12;
                if (HttpClient.DefaultProxy is null) return 13;
                return 100;
            }

            private static int NonValidatedHeaders()
            {
                using var request = new HttpRequestMessage(HttpMethod.Connect, "http://example.test/");
                request.Headers.Protocol = "websocket";
                if (request.Headers.Protocol != "websocket") return 1;
                try
                {
                    request.Headers.Protocol = "a\r\nb";
                    return 2;
                }
                catch (FormatException)
                {
                }
                request.Headers.TryAddWithoutValidation("X-Raw", "not  validated");
                request.Headers.Accept.ParseAdd("text/html");
                request.Headers.Add("Accept", "application/json");
                var headers = request.Headers.NonValidated;
                if (!headers.TryGetValues("X-Raw", out var raw) || raw.Count != 1 || raw.ToString() != "not  validated") return 3;
                if (!headers.TryGetValues("accept", out var accept) || accept.Count != 2) return 4;
                if (!headers.Contains("Accept") || headers.Contains("X-Missing")) return 5;
                var count = 0;
                var values = 0;
                foreach (var pair in headers)
                {
                    count++;
                    foreach (var value in pair.Value) values += value.Length;
                }
                return 100 + count * 10 + values;
            }

            private sealed class FailingStream(Exception failure) : Stream
            {
                public override bool CanRead => true;
                public override bool CanSeek => false;
                public override bool CanWrite => false;
                public override long Length => throw new NotSupportedException();
                public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
                public override void Flush() { }
                public override int Read(byte[] buffer, int offset, int count) => throw failure;
                // Asynchronous reads fail as a transport stream's do. Stream's default ReadAsync would run Read on the
                // thread pool, which a single-threaded WASI program cannot block on.
                public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                    Task.FromException<int>(failure);
                public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                    ValueTask.FromException<int>(failure);
                public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                public override void SetLength(long value) => throw new NotSupportedException();
                public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            }

            private static int Exceptions()
            {
                var status = new HttpRequestException("m", null, HttpStatusCode.NotFound);
                if (status.StatusCode != HttpStatusCode.NotFound || status.Message != "m") return 1;
                var error = new HttpRequestException(HttpRequestError.NameResolutionError, "dns", null, HttpStatusCode.BadGateway);
                if (error.HttpRequestError != HttpRequestError.NameResolutionError || error.StatusCode != HttpStatusCode.BadGateway) return 2;
                var io = new HttpIOException(HttpRequestError.ResponseEnded, "ended");
                if (io.Message != "ended (ResponseEnded)" || io.HttpRequestError != HttpRequestError.ResponseEnded) return 3;
                if (io is not IOException) return 4;
                var protocol = new HttpProtocolException(0x1, "proto", null);
                if (protocol.ErrorCode != 1 || protocol.HttpRequestError != HttpRequestError.HttpProtocolError) return 5;

                using var failing = new StreamContent(new FailingStream(new HttpIOException(HttpRequestError.ResponseEnded, "x")));
                try
                {
                    failing.CopyToAsync(new MemoryStream()).GetAwaiter().GetResult();
                    return 6;
                }
                catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.ResponseEnded && e.InnerException is HttpIOException)
                {
                }
                using var failingSynchronously = new StreamContent(new FailingStream(new IOException("y")));
                try
                {
                    failingSynchronously.CopyTo(new MemoryStream(), null, CancellationToken.None);
                    return 7;
                }
                catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.Unknown && e.InnerException is IOException)
                {
                }
                return 100;
            }

            // GZip and Brotli compress through native zlib and Brotli, which a corpus module does not link; the
            // System.Net.Http library tests run them. Zstandard's content uses no native code on WASI.
            private static int CompressedContent()
            {
                const string text = "hello hello hello hello hello hello";
                // Zstandard is not supported on WASI: its content throws PlatformNotSupportedException where desktop
                // compresses.
                int outcome;
                try
                {
                    using var zstd = new ZstandardCompressedContent(new StringContent(text));
                    outcome = zstd.Headers.ContentEncoding.Contains("zstd") && zstd.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length > 0 ? 1 : 2;
                }
                catch (PlatformNotSupportedException)
                {
                    outcome = 3;
                }
                // SmallestSize selects RFC 9659's window options, which WASI cannot create: the constructor throws
                // PlatformNotSupportedException (NetWasm rethrows a failed static initializer's own exception), and
                // other levels stay usable afterwards.
                int smallest;
                try
                {
                    using var zstd = new ZstandardCompressedContent(new StringContent(text), CompressionLevel.SmallestSize);
                    smallest = 1;
                }
                catch (PlatformNotSupportedException)
                {
                    smallest = 3;
                }
                using var afterwards = new ZstandardCompressedContent(new StringContent(text), CompressionLevel.Fastest);
                if (!afterwards.Headers.ContentEncoding.Contains("zstd")) return 30;
                if (smallest != (OperatingSystem.IsWasi() ? 3 : 1)) return 20 + smallest;
                return outcome == (OperatingSystem.IsWasi() ? 3 : 1) ? 100 : 10 + outcome;
            }

            private static int SocketsHandler()
            {
                using var handler = new SocketsHttpHandler();
                int outcome;
                try
                {
                    handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always;
                    outcome = handler.KeepAlivePingPolicy == HttpKeepAlivePingPolicy.Always ? 1 : 2;
                }
                catch (PlatformNotSupportedException)
                {
                    outcome = 3;
                }
                var policies = new[] { HttpKeepAlivePingPolicy.WithActiveRequests, HttpKeepAlivePingPolicy.Always };
                if ((int)policies[0] != 0 || (int)policies[1] != 1) return 20;
                return outcome == (SocketsHttpHandler.IsSupported ? 1 : 3) ? 100 : 10 + outcome;
            }

            private static int PrimitivesSurface()
            {
                var methods = new[] { DecompressionMethods.All, DecompressionMethods.Brotli, DecompressionMethods.Zstandard };
                if ((int)methods[0] != -1 || (int)methods[1] != 4 || (int)methods[2] != 8) return 1;
                var network = IPNetwork.Parse("192.168.0.0/16");
                if (!network.Contains(IPAddress.Parse("192.168.5.1")) || network.Contains(IPAddress.Parse("10.0.0.1"))) return 2;
                if (network.ToString() != "192.168.0.0/16" || network.PrefixLength != 16 || !network.BaseAddress.Equals(IPAddress.Parse("192.168.0.0"))) return 3;
                // Since .NET 9 the host bits are cleared rather than rejected; a prefix longer than the address is not.
                if (!IPNetwork.TryParse("192.168.1.1/16", out var cleared) || cleared.ToString() != "192.168.0.0/16") return 4;
                if (IPNetwork.TryParse("10.0.0.0/33", out _)) return 10;
                var v6 = IPNetwork.Parse("2001:db8::/32");
                if (!v6.Contains(IPAddress.Parse("2001:db8::1")) || v6.Contains(IPAddress.Parse("2001:db9::1"))) return 5;
                Span<char> buffer = stackalloc char[64];
                if (!v6.TryFormat(buffer, out var written) || buffer.Slice(0, written).ToString() != "2001:db8::/32") return 6;
                try
                {
                    IPNetwork.Parse("bogus");
                    return 7;
                }
                catch (FormatException)
                {
                }
                if (new IPNetwork(IPAddress.Parse("10.0.0.0"), 8) != IPNetwork.Parse("10.0.0.0/8")) return 8;
                // An IPv4-mapped IPv6 address is tested against an IPv4 network as its IPv4 address.
                if (!network.Contains(IPAddress.Parse("::ffff:192.168.5.1")) || network.Contains(IPAddress.Parse("::ffff:10.0.0.1"))) return 9;
                return 100;
            }
        }
        """;
}
