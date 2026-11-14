using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NetSecurityWebSocketsCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WebSocketFramingAuthenticationOptionsAndBoundariesMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "NetSecurityWebSocketsFixture", Source, "NetSecurityWebSocketsFixture.EntryPoint", false, target, 41, [])
        {
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            ObserveMethodName = "Observe",
        }));
    }

    internal const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Specialized;
        using System.IO;
        using System.Net;
        using System.Net.Security;
        using System.Net.WebSockets;
        using System.Security.Authentication;
        using System.Security.Cryptography.X509Certificates;
        using System.Security.Principal;
        using System.Text;
        using System.Threading;
        using System.Threading.Tasks;

        namespace NetSecurityWebSocketsFixture;

        // One direction of an in-memory connection; it records every byte written for wire checks.
        public sealed class Pipe
        {
            private readonly Queue<byte> _bytes = new();
            private TaskCompletionSource<bool>? _waiter;
            private bool _completed;
            public readonly List<byte> Log = new();

            public void Write(ReadOnlySpan<byte> data)
            {
                TaskCompletionSource<bool>? waiter;
                lock (this)
                {
                    foreach (byte b in data)
                    {
                        _bytes.Enqueue(b);
                        Log.Add(b);
                    }
                    waiter = _waiter;
                    _waiter = null;
                }
                waiter?.TrySetResult(true);
            }

            public void Complete()
            {
                TaskCompletionSource<bool>? waiter;
                lock (this)
                {
                    _completed = true;
                    waiter = _waiter;
                    _waiter = null;
                }
                waiter?.TrySetResult(true);
            }

            public async ValueTask<int> ReadAsync(Memory<byte> buffer)
            {
                while (true)
                {
                    Task<bool> wait;
                    lock (this)
                    {
                        if (_bytes.Count > 0)
                        {
                            int n = Math.Min(buffer.Length, _bytes.Count);
                            for (int i = 0; i < n; i++)
                            {
                                buffer.Span[i] = _bytes.Dequeue();
                            }
                            return n;
                        }
                        if (_completed)
                        {
                            return 0;
                        }
                        _waiter ??= new TaskCompletionSource<bool>();
                        wait = _waiter.Task;
                    }
                    await wait.ConfigureAwait(false);
                }
            }
        }

        public sealed class DuplexStream : Stream
        {
            private readonly Pipe _in;
            private readonly Pipe _out;
            public bool Disposed;
            public int Flushes;

            public DuplexStream(Pipe input, Pipe output)
            {
                _in = input;
                _out = output;
            }

            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => Flushes++;
            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                Flushes++;
                return Task.CompletedTask;
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _in.ReadAsync(buffer);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _out.Write(buffer.AsSpan(offset, count));
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                _out.Write(buffer.Span);
                return default;
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                _out.Complete();
                base.Dispose(disposing);
            }
        }

        // A stream that can be read but not written, for argument checks.
        public sealed class ReadOnlyStream : MemoryStream
        {
            public override bool CanWrite => false;
        }

        // A user authenticated stream over the AuthenticatedStream base.
        public sealed class UserAuthenticatedStream : AuthenticatedStream
        {
            public UserAuthenticatedStream(Stream inner, bool leaveOpen) : base(inner, leaveOpen) { }
            public Stream Inner => InnerStream;
            public override bool IsAuthenticated => true;
            public override bool IsMutuallyAuthenticated => false;
            public override bool IsEncrypted => false;
            public override bool IsSigned => true;
            public override bool IsServer => false;
            public override bool CanRead => InnerStream.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => InnerStream.CanWrite;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => InnerStream.Flush();
            public override int Read(byte[] buffer, int offset, int count) => InnerStream.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => InnerStream.Write(buffer, offset, count);
        }

        // A user WebSocket that records what the base memory adapters forward.
        public sealed class ProbeSocket : WebSocket
        {
            public ArraySegment<byte> LastSent;
            public WebSocketMessageType LastType;
            public bool LastEnd;
            public override WebSocketCloseStatus? CloseStatus => null;
            public override string? CloseStatusDescription => null;
            public override string? SubProtocol => "probe";
            public override WebSocketState State => WebSocketState.Open;
            public override void Abort() { }
            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
            public override void Dispose() { }
            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                buffer.Array![buffer.Offset] = 7;
                buffer.Array[buffer.Offset + 1] = 8;
                buffer.Array[buffer.Offset + 2] = 9;
                return Task.FromResult(new WebSocketReceiveResult(3, WebSocketMessageType.Binary, false));
            }
            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                LastSent = buffer;
                LastType = messageType;
                LastEnd = endOfMessage;
                return Task.CompletedTask;
            }
            public static bool InvalidStateThrows()
            {
                ThrowOnInvalidState(WebSocketState.Open, WebSocketState.Open, WebSocketState.CloseSent);
                try
                {
                    ThrowOnInvalidState(WebSocketState.Closed, WebSocketState.Open);
                    return false;
                }
                catch (WebSocketException e)
                {
                    return e.WebSocketErrorCode == WebSocketError.InvalidState;
                }
            }
            public static bool Terminal(WebSocketState state) => IsStateTerminal(state);
        }

        public sealed class UserContext : WebSocketContext
        {
            public override Uri RequestUri => new Uri("ws://example.com/chat");
            public override NameValueCollection Headers => new NameValueCollection { { "Origin", "example.com" } };
            public override string Origin => "example.com";
            public override IEnumerable<string> SecWebSocketProtocols => new[] { "chat" };
            public override string SecWebSocketVersion => "13";
            public override string SecWebSocketKey => "key";
            public override CookieCollection CookieCollection => new CookieCollection();
            public override IPrincipal User => new GenericPrincipal(new GenericIdentity("u"), null);
            public override bool IsAuthenticated => true;
            public override bool IsLocal => true;
            public override bool IsSecureConnection => false;
            public override WebSocket WebSocket => new ProbeSocket();
        }

        public static class EntryPoint
        {
            private static Task<int>? s_pending;
            public static Task<int>? Pending => s_pending;

            static bool Throws<T>(Action action) where T : Exception
            {
                try { action(); return false; } catch (T) { return true; }
            }

            static async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
            {
                try { await action().ConfigureAwait(false); return false; } catch (T) { return true; }
            }

            public static int Run(int input)
            {
                int code = Synchronous();
                s_pending = code != 0 ? Task.FromResult(code) : RunAsync();
                return input + 1;
            }

            public static int Observe() =>
                s_pending is { IsCompleted: true } pending ? (pending.IsFaulted ? 99 : pending.Result == 0 ? 42 : pending.Result) : 98;

            static int Synchronous()
            {
                // Result values and enums keep their reference shapes and validation.
                var result = new WebSocketReceiveResult(5, WebSocketMessageType.Text, true, WebSocketCloseStatus.NormalClosure, "done");
                if (result.Count != 5 || result.CloseStatus != WebSocketCloseStatus.NormalClosure || result.CloseStatusDescription != "done") return 1;
                if (new WebSocketReceiveResult(1, (WebSocketMessageType)9, false).MessageType != (WebSocketMessageType)9) return 2;
                if (!Throws<ArgumentOutOfRangeException>(() => new WebSocketReceiveResult(-1, WebSocketMessageType.Binary, true))) return 3;
                var value = new ValueWebSocketReceiveResult(int.MaxValue, WebSocketMessageType.Close, true);
                if (value.Count != int.MaxValue || !value.EndOfMessage || value.MessageType != WebSocketMessageType.Close || default(ValueWebSocketReceiveResult).EndOfMessage) return 4;
                if (!Throws<ArgumentOutOfRangeException>(() => new ValueWebSocketReceiveResult(1, (WebSocketMessageType)3, true))) return 5;
                if ((int)WebSocketCloseStatus.MessageTooBig != 1009 || (int)WebSocketError.HeaderError != 7 || (int)WebSocketState.Aborted != 6) return 6;

                // Buffer factories and state helpers.
                if (WebSocket.CreateClientBuffer(10, 20).Count != 20 || WebSocket.CreateServerBuffer(16).Count != 16) return 7;
                if (!Throws<ArgumentOutOfRangeException>(() => WebSocket.CreateClientBuffer(0, 1))) return 8;
                if (!ProbeSocket.InvalidStateThrows() || !ProbeSocket.Terminal(WebSocketState.Aborted) || ProbeSocket.Terminal(WebSocketState.CloseSent)) return 9;
                if (WebSocket.DefaultKeepAliveInterval != TimeSpan.FromSeconds(30)) return 10;
        #pragma warning disable CS0618
                if (!Throws<PlatformNotSupportedException>(WebSocket.RegisterPrefixes)) return 11;
        #pragma warning restore CS0618

                // Factory argument checks.
                var pair = new Pipe();
                if (!Throws<ArgumentNullException>(() => WebSocket.CreateFromStream(null!, false, null, Timeout.InfiniteTimeSpan))) return 12;
                if (!Throws<ArgumentException>(() => WebSocket.CreateFromStream(new ReadOnlyStream(), false, null, Timeout.InfiniteTimeSpan))) return 13;
                if (!Throws<ArgumentException>(() => WebSocket.CreateFromStream(new DuplexStream(pair, pair), false, "bad protocol", Timeout.InfiniteTimeSpan))) return 14;
                if (!Throws<ArgumentOutOfRangeException>(() => WebSocket.CreateFromStream(new DuplexStream(pair, pair), false, null, TimeSpan.FromSeconds(-2)))) return 15;
                if (!Throws<ArgumentOutOfRangeException>(() => WebSocket.CreateClientWebSocket(new DuplexStream(pair, pair), null, 0, 16, Timeout.InfiniteTimeSpan, false, default))) return 16;

                // Exceptions keep their error codes, messages and inner exceptions.
                var inner = new InvalidOperationException("inner");
                var withInner = new WebSocketException(WebSocketError.ConnectionClosedPrematurely, inner);
                if (withInner.WebSocketErrorCode != WebSocketError.ConnectionClosedPrematurely || !ReferenceEquals(withInner.InnerException, inner)) return 17;
                var native = new WebSocketException(-7, "native");
                if (native.ErrorCode != -7 || native.HResult != -7 || native.WebSocketErrorCode != WebSocketError.NativeError || native.Message != "native") return 18;
                var success = new WebSocketException(3, "ok");
                if (success.ErrorCode != 3 || success.WebSocketErrorCode != WebSocketError.Success || success.HResult != unchecked((int)0x80004005)) return 19;
                if (new WebSocketException(WebSocketError.Faulted, -5).HResult != -5) return 20;
                if (new WebSocketException((string?)null).Message != new WebSocketException(WebSocketError.Success, (string?)null).Message) return 21;
        #if !DESKTOP_ORACLE
                // No native operation produced these errors, so NativeErrorCode is 0 rather than an ambient last error.
                if (withInner.ErrorCode != 0 || new WebSocketException("m").ErrorCode != 0) return 22;
                // The parameterless constructor reads the last P/Invoke error, as upstream's does.
                if (new WebSocketException().ErrorCode != System.Runtime.InteropServices.Marshal.GetLastPInvokeError()) return 23;
        #endif

                // Context data is supplied by subclasses.
                WebSocketContext context = new UserContext();
                if (context.RequestUri.Host != "example.com" || context.SecWebSocketVersion != "13" || !context.IsLocal || context.WebSocket.SubProtocol != "probe") return 24;
                var basic = new HttpListenerBasicIdentity("user", "secret");
                if (basic.Name != "user" || basic.Password != "secret" || basic.AuthenticationType != "Basic" || !basic.IsAuthenticated) return 25;

                // Authenticated stream base: argument checks, ownership and abstract dispatch.
                if (!Throws<ArgumentNullException>(() => new UserAuthenticatedStream(null!, false))) return 26;
                if (!Throws<ArgumentNullException>(() => new UserAuthenticatedStream(Stream.Null, false))) return 27;
                if (!Throws<ArgumentException>(() => new UserAuthenticatedStream(new ReadOnlyStream(), false))) return 28;
                var kept = new DuplexStream(new Pipe(), new Pipe());
                var leaveOpen = new UserAuthenticatedStream(kept, true);
                if (!ReferenceEquals(leaveOpen.Inner, kept) || !leaveOpen.LeaveInnerStreamOpen || !leaveOpen.IsSigned || leaveOpen.IsEncrypted) return 29;
                leaveOpen.Dispose();
                if (kept.Disposed || kept.Flushes != 1) return 30;
                var owned = new DuplexStream(new Pipe(), new Pipe());
                new UserAuthenticatedStream(owned, false).Dispose();
                if (!owned.Disposed) return 31;

                // Options store values and callbacks without acting on them.
                var client = new SslClientAuthenticationOptions();
        #pragma warning disable SYSLIB0040
                if (!client.AllowRenegotiation || client.EncryptionPolicy != EncryptionPolicy.RequireEncryption || client.CertificateRevocationCheckMode != X509RevocationMode.NoCheck ||
                    client.EnabledSslProtocols != SslProtocols.None || client.ApplicationProtocols != null || client.TargetHost != null) return 32;
                client.EncryptionPolicy = EncryptionPolicy.AllowNoEncryption;
        #pragma warning restore SYSLIB0040
                if (!Throws<ArgumentException>(() => client.EncryptionPolicy = (EncryptionPolicy)9)) return 33;
                if (!Throws<ArgumentException>(() => client.CertificateRevocationCheckMode = (X509RevocationMode)5)) return 34;
                bool invoked = false;
                RemoteCertificateValidationCallback validation = (sender, certificate, chain, errors) => invoked = true;
                var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 };
                client.RemoteCertificateValidationCallback = validation;
                client.ApplicationProtocols = protocols;
                client.EnabledSslProtocols = (SslProtocols)12345;
                protocols.Add(SslApplicationProtocol.Http11);
                if (invoked || !ReferenceEquals(client.RemoteCertificateValidationCallback, validation) || client.ApplicationProtocols.Count != 2 || (int)client.EnabledSslProtocols != 12345) return 35;
                var server = new SslServerAuthenticationOptions();
                if (server.AllowRenegotiation || server.ClientCertificateRequired || server.ServerCertificate != null || server.CertificateRevocationCheckMode != X509RevocationMode.NoCheck) return 36;

                // Application protocols compare by bytes.
                if (SslApplicationProtocol.Http2.ToString() != "h2" || SslApplicationProtocol.Http11 != new SslApplicationProtocol("http/1.1")) return 37;
                var custom = new SslApplicationProtocol(new byte[] { 0x61, 0x62 });
                if (custom.ToString() != "ab" || custom.Protocol.Length != 2 || custom.GetHashCode() != new SslApplicationProtocol("ab").GetHashCode()) return 38;
                if (!Throws<ArgumentException>(() => new SslApplicationProtocol(Array.Empty<byte>())) || !Throws<ArgumentNullException>(() => new SslApplicationProtocol((string)null!))) return 39;
                if ((int)ProtectionLevel.EncryptAndSign != 2 || (int)EncryptionPolicy.RequireEncryption != 0) return 40;

                // Concrete TLS and Negotiate streams check their arguments as upstream does.
                if (!Throws<ArgumentNullException>(() => new SslStream(null!))) return 41;
                if (!Throws<ArgumentException>(() => new NegotiateStream(new ReadOnlyStream()))) return 42;
        #if !DESKTOP_ORACLE
                // NetWasm has no TLS or Negotiate backend: construction throws before using the stream.
                var untouched = new DuplexStream(new Pipe(), new Pipe());
                if (!Throws<PlatformNotSupportedException>(() => new SslStream(untouched, false))) return 43;
                if (!Throws<PlatformNotSupportedException>(() => new NegotiateStream(untouched))) return 44;
                if (untouched.Disposed || untouched.Flushes != 0) return 45;
        #endif

                // Client options: owner-created defaults, validation and identity.
                var socket = new ClientWebSocket();
                var options = socket.Options;
                if (socket.State != WebSocketState.None || socket.CloseStatus != null || socket.SubProtocol != null) return 46;
                if (options.KeepAliveInterval != WebSocket.DefaultKeepAliveInterval || options.Proxy == null || options.UseDefaultCredentials || options.Credentials != null || options.Cookies != null) return 47;
                if (!ReferenceEquals(options.ClientCertificates, options.ClientCertificates) || options.ClientCertificates.Count != 0) return 48;
                options.AddSubProtocol("chat");
                if (!Throws<ArgumentException>(() => options.AddSubProtocol("CHAT")) || !Throws<ArgumentException>(() => options.AddSubProtocol("a b"))) return 49;
                if (!Throws<ArgumentOutOfRangeException>(() => options.SetBuffer(0, 16)) || !Throws<ArgumentOutOfRangeException>(() => options.KeepAliveInterval = TimeSpan.FromSeconds(-2))) return 50;
                options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
                options.SetRequestHeader("X-Test", "1");
                if (!Throws<ArgumentNullException>(() => options.ClientCertificates = null!)) return 51;
                if (!Throws<InvalidOperationException>(() => socket.SendAsync(new ArraySegment<byte>(new byte[1]), WebSocketMessageType.Binary, true, default))) return 52;
                if (!Throws<ArgumentNullException>(() => socket.ConnectAsync(null!, default)) ||
                    !Throws<ArgumentException>(() => socket.ConnectAsync(new Uri("/relative", UriKind.Relative), default)) ||
                    !Throws<ArgumentException>(() => socket.ConnectAsync(new Uri("http://example.com/"), default))) return 53;
                socket.Abort();
                socket.Abort();
                socket.Dispose();
                if (socket.State != WebSocketState.Closed || !Throws<ObjectDisposedException>(() => socket.SendAsync(new ArraySegment<byte>(new byte[1]), WebSocketMessageType.Binary, true, default))) return 54;
                return 0;
            }

            static WebSocket[] Connect(out DuplexStream clientStream, out Pipe clientToServer, out Pipe serverToClient)
            {
                clientToServer = new Pipe();
                serverToClient = new Pipe();
                clientStream = new DuplexStream(serverToClient, clientToServer);
                var serverStream = new DuplexStream(clientToServer, serverToClient);
                return new[]
                {
                    WebSocket.CreateFromStream(clientStream, false, "chat", Timeout.InfiniteTimeSpan),
                    WebSocket.CreateFromStream(serverStream, true, "chat", TimeSpan.Zero),
                };
            }

            static async Task<int> RunAsync()
            {
                // Memory adapters forward array-backed buffers to the ArraySegment overloads.
                var probe = new ProbeSocket();
                var probeBuffer = new byte[8];
                var probed = await probe.ReceiveAsync(probeBuffer.AsMemory(2, 4), default).ConfigureAwait(false);
                if (probed.Count != 3 || probed.EndOfMessage || probeBuffer[2] != 7 || probeBuffer[4] != 9) return 100;
                await probe.SendAsync(new ReadOnlyMemory<byte>(probeBuffer, 1, 3), WebSocketMessageType.Text, true, default).ConfigureAwait(false);
                if (!ReferenceEquals(probe.LastSent.Array, probeBuffer) || probe.LastSent.Offset != 1 || probe.LastSent.Count != 3 || probe.LastType != WebSocketMessageType.Text || !probe.LastEnd) return 101;

                var sockets = Connect(out DuplexStream clientStream, out Pipe clientToServer, out Pipe serverToClient);
                WebSocket client = sockets[0], server = sockets[1];
                if (client.State != WebSocketState.Open || client.SubProtocol != "chat") return 102;

                // Client frames are masked; the payload unmasks to the text sent.
                await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hello")), WebSocketMessageType.Text, true, default).ConfigureAwait(false);
                var wire = clientToServer.Log;
                if (wire.Count != 11 || wire[0] != 0x81 || wire[1] != (0x80 | 5)) return 103;
                for (int i = 0; i < 5; i++)
                {
                    if ((byte)(wire[6 + i] ^ wire[2 + (i % 4)]) != (byte)"hello"[i]) return 104;
                }
                var buffer = new byte[64];
                var received = await server.ReceiveAsync(new ArraySegment<byte>(buffer), default).ConfigureAwait(false);
                if (received.Count != 5 || received.MessageType != WebSocketMessageType.Text || !received.EndOfMessage || Encoding.UTF8.GetString(buffer, 0, 5) != "hello") return 105;

                // Server frames are unmasked.
                await server.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hi")), WebSocketMessageType.Text, true, default).ConfigureAwait(false);
                if (serverToClient.Log.Count != 4 || serverToClient.Log[0] != 0x81 || serverToClient.Log[1] != 2 || serverToClient.Log[2] != (byte)'h') return 106;
                var reply = await client.ReceiveAsync(buffer.AsMemory(), default).ConfigureAwait(false);
                if (reply.Count != 2 || reply.MessageType != WebSocketMessageType.Text || !reply.EndOfMessage) return 107;

                // Fragments arrive as separate results; continuation frames carry the message type.
                await client.SendAsync(new ArraySegment<byte>(new byte[] { 1, 2 }), WebSocketMessageType.Binary, false, default).ConfigureAwait(false);
                await client.SendAsync(new ArraySegment<byte>(new byte[] { 3, 4 }), WebSocketMessageType.Binary, true, default).ConfigureAwait(false);
                if (wire[11] != 0x02 || wire[19] != 0x80) return 108;
                var first = await server.ReceiveAsync(new ArraySegment<byte>(buffer), default).ConfigureAwait(false);
                var second = await server.ReceiveAsync(new ArraySegment<byte>(buffer, 2, 62), default).ConfigureAwait(false);
                if (first.Count != 2 || first.EndOfMessage || second.Count != 2 || !second.EndOfMessage || second.MessageType != WebSocketMessageType.Binary || buffer[3] != 4) return 109;

                // A small receive buffer reads a frame in parts.
                await server.SendAsync(new ArraySegment<byte>(new byte[10]), WebSocketMessageType.Binary, true, default).ConfigureAwait(false);
                var part1 = await client.ReceiveAsync(new ArraySegment<byte>(new byte[4]), default).ConfigureAwait(false);
                var part2 = await client.ReceiveAsync(new ArraySegment<byte>(new byte[4]), default).ConfigureAwait(false);
                var part3 = await client.ReceiveAsync(new ArraySegment<byte>(new byte[4]), default).ConfigureAwait(false);
                if (part1.Count != 4 || part1.EndOfMessage || part2.Count != 4 || part3.Count != 2 || !part3.EndOfMessage) return 110;

                // Invalid arguments and states.
                if (!Throws<ArgumentException>(() => client.SendAsync(new ArraySegment<byte>(new byte[1]), WebSocketMessageType.Close, true, default))) return 111;
                if (!Throws<ArgumentException>(() => client.CloseAsync(WebSocketCloseStatus.Empty, "reason", default))) return 112;

                // Close handshake.
                var clientClose = client.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", default);
                var closing = await server.ReceiveAsync(new ArraySegment<byte>(buffer), default).ConfigureAwait(false);
                if (closing.MessageType != WebSocketMessageType.Close || closing.CloseStatus != WebSocketCloseStatus.NormalClosure || closing.CloseStatusDescription != "bye" || server.State != WebSocketState.CloseReceived) return 113;
                await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", default).ConfigureAwait(false);
                await clientClose.ConfigureAwait(false);
                if (client.State != WebSocketState.Closed || server.State != WebSocketState.Closed || client.CloseStatus != WebSocketCloseStatus.NormalClosure || client.CloseStatusDescription != "bye") return 114;
                if (!await ThrowsAsync<WebSocketException>(() => client.SendAsync(new ArraySegment<byte>(new byte[1]), WebSocketMessageType.Binary, true, default)).ConfigureAwait(false)) return 115;
                client.Dispose();
                if (!clientStream.Disposed) return 116;

                // A server rejects an unmasked frame as a protocol error.
                var unmasked = Connect(out _, out Pipe raw, out _);
                // A server reads at least the six bytes a masked header needs before it parses one.
                raw.Write(new byte[] { 0x81, 0x04, (byte)'a', (byte)'b', (byte)'c', (byte)'d' });
                try
                {
                    await unmasked[1].ReceiveAsync(new ArraySegment<byte>(buffer), default).ConfigureAwait(false);
                    return 117;
                }
                catch (WebSocketException e) when (e.WebSocketErrorCode == WebSocketError.Faulted)
                {
                }

                // Text payloads must be valid UTF-8.
                var invalid = Connect(out _, out _, out _);
                await invalid[0].SendAsync(new ArraySegment<byte>(new byte[] { 0xFF, 0xFE }), WebSocketMessageType.Text, true, default).ConfigureAwait(false);
                try
                {
                    await invalid[1].ReceiveAsync(new ArraySegment<byte>(buffer), default).ConfigureAwait(false);
                    return 118;
                }
                catch (WebSocketException e) when (e.WebSocketErrorCode == WebSocketError.Faulted)
                {
                }

                // Abort ends the socket.
                var aborted = Connect(out _, out _, out _);
                aborted[1].Abort();
                if (aborted[1].State != WebSocketState.Aborted) return 119;

                // A canceled connect freezes the options and leaves the client closed.
                var cancelled = new ClientWebSocket();
                using (var cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    if (!await ThrowsAsync<OperationCanceledException>(() => cancelled.ConnectAsync(new Uri("ws://example.com/"), cts.Token)).ConfigureAwait(false)) return 120;
                }
                if (cancelled.State != WebSocketState.Closed || !Throws<InvalidOperationException>(() => cancelled.Options.KeepAliveInterval = TimeSpan.Zero)) return 121;
                if (!Throws<ObjectDisposedException>(() => cancelled.ConnectAsync(new Uri("ws://example.com/"), default))) return 122;
        #if !DESKTOP_ORACLE
                // NetWasm has no transport to upgrade: connecting fails catchably and the client ends closed.
                var unsupported = new ClientWebSocket();
                if (!await ThrowsAsync<PlatformNotSupportedException>(() => unsupported.ConnectAsync(new Uri("wss://example.com/"), default)).ConfigureAwait(false)) return 123;
                if (unsupported.State != WebSocketState.Closed || !Throws<ObjectDisposedException>(() => unsupported.ConnectAsync(new Uri("ws://example.com/"), default))) return 124;
        #endif
                return 0;
            }
        }
        """;
}
