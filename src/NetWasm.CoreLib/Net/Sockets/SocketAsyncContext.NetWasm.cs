// NetWasm's counterpart of dotnet/runtime 474ed963699fc29809015392738a15231aaae2de's
// SocketAsyncContext.Unix.cs: the methods SocketPal and SocketAsyncEventArgs call, with the same
// signatures, over the platform network (PlatformServices.Network) instead of an epoll readiness
// engine. The network completes operations, like IOCP, so a blocking method calls the network's
// blocking call, and an asynchronous method starts its async call: when that has already completed
// the method returns the result, and otherwise it returns IOPending and the callback reports the
// result. Closing the socket aborts the operations in flight. Socket addresses are SocketAddressPal's
// layout. Timeouts are not supported (Socket rejects them), so the timeout arguments are unused.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using PlatformServices = System.Runtime.InteropServices.PlatformServices;

namespace System.Net.Sockets
{
    internal sealed class SocketAsyncContext
    {
        private readonly SafeSocketHandle _socket;
        private CancellationTokenSource? _abort;
        private int _pending;
        private bool _nonBlocking;

        public SocketAsyncContext(SafeSocketHandle socket) => _socket = socket;

        public bool IsHandleNonBlocking => _nonBlocking;

        private static INetwork Network => PlatformServices.Network;

        private long Handle => _socket.DangerousGetHandle();

        private bool IsStream => _socket.IsSocket && Interop.Sys.GetSocketType(
            _socket, out _, out var type, out _, out _) == Interop.Error.SUCCESS && type == SocketType.Stream;

        // Aborts the operations in flight; returns whether there were any.
        public bool StopAndAbort()
        {
            var aborted = _pending > 0;
            _abort?.Cancel();
            return aborted;
        }

        public void SetHandleNonBlocking() => _nonBlocking = true;

        public void SetHandleBlocking() => _nonBlocking = false;

        public SocketError Accept(Memory<byte> socketAddress, out int socketAddressLen, out IntPtr acceptedFd)
        {
            var error = Network.Accept(Handle, out var accepted, out var remote);
            return Accepted(new NetworkAcceptResult(error, accepted, remote), socketAddress, out socketAddressLen, out acceptedFd);
        }

        public SocketError AcceptAsync(Memory<byte> socketAddress, out int socketAddressLen, out IntPtr acceptedFd,
            Action<IntPtr, Memory<byte>, SocketError> callback, CancellationToken cancellationToken)
        {
            if (TryComplete(Abortable(token => Network.AcceptAsync(Handle, token), cancellationToken), out var result, completed =>
            {
                var error = Accepted(completed, socketAddress, out var length, out var accepted);
                callback(accepted, socketAddress[..length], error);
            }))
            {
                return Accepted(result, socketAddress, out socketAddressLen, out acceptedFd);
            }

            (socketAddressLen, acceptedFd) = (0, IntPtr.Zero);
            return SocketError.IOPending;
        }

        public SocketError Connect(Memory<byte> socketAddress) => Network.Connect(Handle, EndPoint(socketAddress));

        // Data given with the connect (TCP Fast Open's on Unix) is sent once connected.
        public SocketError ConnectAsync(Memory<byte> socketAddress, Action<int, Memory<byte>, SocketFlags, SocketError> callback,
            Memory<byte> buffer, out int sentBytes, CancellationToken cancellationToken)
        {
            var remote = EndPoint(socketAddress);
            if (TryComplete(Abortable(token => ConnectThenSend(remote, buffer, token), cancellationToken), out var result,
                completed => callback(completed.BytesTransferred, Memory<byte>.Empty, SocketFlags.None, completed.Error)))
            {
                sentBytes = result.BytesTransferred;
                return result.Error;
            }

            sentBytes = 0;
            return SocketError.IOPending;
        }

        public SocketError Receive(Memory<byte> buffer, SocketFlags flags, int timeout, out int bytesReceived) =>
            Receive(buffer.Span, flags, timeout, out bytesReceived);

        public SocketError Receive(Span<byte> buffer, SocketFlags flags, int timeout, out int bytesReceived) =>
            Network.Receive(Handle, buffer, out bytesReceived);

        public SocketError Receive(IList<ArraySegment<byte>> buffers, SocketFlags flags, int timeout, out int bytesReceived)
        {
            var data = new byte[Length(buffers)];
            var error = Network.Receive(Handle, data, out bytesReceived);
            Scatter(data.AsSpan(0, bytesReceived), buffers);
            return error;
        }

        public SocketError ReceiveAsync(Memory<byte> buffer, SocketFlags flags, out int bytesReceived,
            Action<int, Memory<byte>, SocketFlags, SocketError> callback, CancellationToken cancellationToken = default) =>
            ReceiveAsync(buffer, flags, out bytesReceived, out _, callback, cancellationToken);

        public SocketError ReceiveAsync(Memory<byte> buffer, SocketFlags flags, out int bytesReceived, out SocketFlags receivedFlags,
            Action<int, Memory<byte>, SocketFlags, SocketError> callback, CancellationToken cancellationToken)
        {
            receivedFlags = SocketFlags.None;
            return Transfer(Abortable(token => Network.ReceiveAsync(Handle, buffer, token), cancellationToken), out bytesReceived,
                completed => callback(completed.BytesTransferred, Memory<byte>.Empty, SocketFlags.None, completed.Error));
        }

        public SocketError ReceiveAsync(IList<ArraySegment<byte>> buffers, SocketFlags flags, out int bytesReceived,
            out SocketFlags receivedFlags, Action<int, Memory<byte>, SocketFlags, SocketError> callback)
        {
            receivedFlags = SocketFlags.None;
            var data = new byte[Length(buffers)];
            return Transfer(Abortable(token => Network.ReceiveAsync(Handle, data, token), default), out bytesReceived,
                completed =>
                {
                    Scatter(data.AsSpan(0, completed.BytesTransferred), buffers);
                    callback(completed.BytesTransferred, Memory<byte>.Empty, SocketFlags.None, completed.Error);
                },
                completed => Scatter(data.AsSpan(0, completed.BytesTransferred), buffers));
        }

        public SocketError ReceiveFrom(Memory<byte> buffer, ref SocketFlags flags, Memory<byte> socketAddress, out int socketAddressLen,
            int timeout, out int bytesReceived) =>
            ReceiveFrom(buffer.Span, ref flags, socketAddress, out socketAddressLen, timeout, out bytesReceived);

        public SocketError ReceiveFrom(Span<byte> buffer, ref SocketFlags flags, Memory<byte> socketAddress, out int socketAddressLen,
            int timeout, out int bytesReceived)
        {
            flags = SocketFlags.None;
            if (IsStream)
            {
                socketAddressLen = 0;
                return Network.Receive(Handle, buffer, out bytesReceived);
            }

            var error = Network.ReceiveFrom(Handle, buffer, out bytesReceived, out var remote);
            socketAddressLen = Serialize(remote, socketAddress.Span);
            return error;
        }

        public SocketError ReceiveFrom(IList<ArraySegment<byte>> buffers, ref SocketFlags flags, Memory<byte> socketAddress,
            out int socketAddressLen, int timeout, out int bytesReceived)
        {
            var data = new byte[Length(buffers)];
            var error = ReceiveFrom(data.AsSpan(), ref flags, socketAddress, out socketAddressLen, timeout, out bytesReceived);
            Scatter(data.AsSpan(0, bytesReceived), buffers);
            return error;
        }

        public SocketError ReceiveFromAsync(Memory<byte> buffer, SocketFlags flags, Memory<byte> socketAddress, out int socketAddressLen,
            out int bytesReceived, out SocketFlags receivedFlags, Action<int, Memory<byte>, SocketFlags, SocketError> callback,
            CancellationToken cancellationToken = default)
        {
            receivedFlags = SocketFlags.None;
            var error = Transfer(Abortable(token => ReceiveFromAsync(buffer, token), cancellationToken), out bytesReceived,
                completed => callback(completed.BytesTransferred, socketAddress[..Serialize(completed.RemoteEndPoint, socketAddress.Span)],
                    SocketFlags.None, completed.Error),
                completed => Serialize(completed.RemoteEndPoint, socketAddress.Span), out var remote);
            socketAddressLen = remote;
            return error;
        }

        public SocketError ReceiveFromAsync(IList<ArraySegment<byte>> buffers, SocketFlags flags, Memory<byte> socketAddress,
            out int socketAddressLen, out int bytesReceived, out SocketFlags receivedFlags,
            Action<int, Memory<byte>, SocketFlags, SocketError> callback)
        {
            receivedFlags = SocketFlags.None;
            var data = new byte[Length(buffers)];
            var error = Transfer(Abortable(token => ReceiveFromAsync(data, token), default), out bytesReceived,
                completed =>
                {
                    Scatter(data.AsSpan(0, completed.BytesTransferred), buffers);
                    callback(completed.BytesTransferred, socketAddress[..Serialize(completed.RemoteEndPoint, socketAddress.Span)],
                        SocketFlags.None, completed.Error);
                },
                completed =>
                {
                    Scatter(data.AsSpan(0, completed.BytesTransferred), buffers);
                    return Serialize(completed.RemoteEndPoint, socketAddress.Span);
                }, out var remote);
            socketAddressLen = remote;
            return error;
        }

        // The network does not report a datagram's destination, so its packet information is empty.
        public SocketError ReceiveMessageFrom(Memory<byte> buffer, ref SocketFlags flags, Memory<byte> socketAddress,
            out int socketAddressLen, bool isIPv4, bool isIPv6, int timeout, out IPPacketInformation ipPacketInformation,
            out int bytesReceived) =>
            ReceiveMessageFrom(buffer.Span, ref flags, socketAddress, out socketAddressLen, isIPv4, isIPv6, timeout,
                out ipPacketInformation, out bytesReceived);

        public SocketError ReceiveMessageFrom(Span<byte> buffer, ref SocketFlags flags, Memory<byte> socketAddress,
            out int socketAddressLen, bool isIPv4, bool isIPv6, int timeout, out IPPacketInformation ipPacketInformation,
            out int bytesReceived)
        {
            ipPacketInformation = default;
            return ReceiveFrom(buffer, ref flags, socketAddress, out socketAddressLen, timeout, out bytesReceived);
        }

        public SocketError ReceiveMessageFromAsync(Memory<byte> buffer, IList<ArraySegment<byte>>? buffers, SocketFlags flags,
            Memory<byte> socketAddress, out int socketAddressLen, bool isIPv4, bool isIPv6, out int bytesReceived,
            out SocketFlags receivedFlags, out IPPacketInformation ipPacketInformation,
            Action<int, Memory<byte>, SocketFlags, IPPacketInformation, SocketError> callback, CancellationToken cancellationToken)
        {
            ipPacketInformation = default;
            void Completed(int bytes, Memory<byte> address, SocketFlags received, SocketError error) =>
                callback(bytes, address, received, default, error);
            return buffers is null
                ? ReceiveFromAsync(buffer, flags, socketAddress, out socketAddressLen, out bytesReceived, out receivedFlags, Completed,
                    cancellationToken)
                : ReceiveFromAsync(buffers, flags, socketAddress, out socketAddressLen, out bytesReceived, out receivedFlags, Completed);
        }

        public SocketError Send(ReadOnlySpan<byte> buffer, SocketFlags flags, int timeout, out int bytesSent) =>
            Network.Send(Handle, buffer, out bytesSent);

        public SocketError Send(byte[] buffer, int offset, int count, SocketFlags flags, int timeout, out int bytesSent) =>
            Network.Send(Handle, buffer.AsSpan(offset, count), out bytesSent);

        public SocketError Send(IList<ArraySegment<byte>> buffers, SocketFlags flags, int timeout, out int bytesSent) =>
            Network.Send(Handle, Gather(buffers), out bytesSent);

        public SocketError SendAsync(Memory<byte> buffer, int offset, int count, SocketFlags flags, out int bytesSent,
            Action<int, Memory<byte>, SocketFlags, SocketError> callback, CancellationToken cancellationToken) =>
            Transfer(Abortable(token => Network.SendAsync(Handle, buffer.Slice(offset, count), token), cancellationToken), out bytesSent,
                completed => callback(completed.BytesTransferred, Memory<byte>.Empty, SocketFlags.None, completed.Error));

        public SocketError SendAsync(IList<ArraySegment<byte>> buffers, SocketFlags flags, out int bytesSent,
            Action<int, Memory<byte>, SocketFlags, SocketError> callback)
        {
            var data = Gather(buffers);
            return Transfer(Abortable(token => Network.SendAsync(Handle, data, token), default), out bytesSent,
                completed => callback(completed.BytesTransferred, Memory<byte>.Empty, SocketFlags.None, completed.Error));
        }

        public SocketError SendTo(byte[] buffer, int offset, int count, SocketFlags flags, Memory<byte> socketAddress, int timeout,
            out int bytesSent) =>
            SendTo(buffer.AsSpan(offset, count), flags, socketAddress, timeout, out bytesSent);

        public SocketError SendTo(ReadOnlySpan<byte> buffer, SocketFlags flags, Memory<byte> socketAddress, int timeout, out int bytesSent) =>
            IsStream
                ? Network.Send(Handle, buffer, out bytesSent)
                : Network.SendTo(Handle, buffer, OptionalEndPoint(socketAddress), out bytesSent);

        public SocketError SendTo(IList<ArraySegment<byte>> buffers, SocketFlags flags, Memory<byte> socketAddress, int timeout,
            out int bytesSent) =>
            SendTo(Gather(buffers), flags, socketAddress, timeout, out bytesSent);

        public SocketError SendToAsync(Memory<byte> buffer, int offset, int count, SocketFlags flags, Memory<byte> socketAddress,
            ref int bytesSent, Action<int, Memory<byte>, SocketFlags, SocketError> callback, CancellationToken cancellationToken = default)
        {
            var error = SendToAsync(buffer.Slice(offset, count), socketAddress, out var sent, callback, cancellationToken);
            bytesSent += sent;
            return error;
        }

        public SocketError SendToAsync(IList<ArraySegment<byte>> buffers, SocketFlags flags, Memory<byte> socketAddress,
            out int bytesSent, Action<int, Memory<byte>, SocketFlags, SocketError> callback) =>
            SendToAsync(Gather(buffers), socketAddress, out bytesSent, callback, default);

        public SocketError SendFile(SafeFileHandle fileHandle, long offset, long count, int timeout, out long bytesSent)
        {
            bytesSent = 0;
            var chunk = new byte[(int)Math.Min(count, 65536)];
            while (bytesSent < count)
            {
                var read = ReadFile(fileHandle, chunk.AsSpan(0, (int)Math.Min(chunk.Length, count - bytesSent)), offset + bytesSent);
                if (read == 0)
                {
                    break;
                }

                var error = Network.Send(Handle, chunk.AsSpan(0, read), out var sent);
                bytesSent += sent;
                if (error != SocketError.Success)
                {
                    return error;
                }
            }

            return SocketError.Success;
        }

        public SocketError SendFileAsync(SafeFileHandle fileHandle, long offset, long count, out long bytesSent,
            Action<long, SocketError> callback, CancellationToken cancellationToken = default)
        {
            if (TryComplete(Abortable(token => SendFileAsync(fileHandle, offset, count, token), cancellationToken), out var result,
                completed => callback(completed.Sent, completed.Error)))
            {
                bytesSent = result.Sent;
                return result.Error;
            }

            bytesSent = 0;
            return SocketError.IOPending;
        }

        private SocketError SendToAsync(ReadOnlyMemory<byte> data, Memory<byte> socketAddress, out int bytesSent,
            Action<int, Memory<byte>, SocketFlags, SocketError> callback, CancellationToken cancellationToken)
        {
            var remote = IsStream ? null : OptionalEndPoint(socketAddress);
            var stream = IsStream;
            return Transfer(Abortable(token => stream ? Network.SendAsync(Handle, data, token) : Network.SendToAsync(Handle, data, remote, token),
                cancellationToken), out bytesSent,
                completed => callback(completed.BytesTransferred, Memory<byte>.Empty, SocketFlags.None, completed.Error));
        }

        private ValueTask<NetworkTransferResult> ReceiveFromAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            IsStream ? Network.ReceiveAsync(Handle, buffer, cancellationToken) : Network.ReceiveFromAsync(Handle, buffer, cancellationToken);

        private async ValueTask<NetworkTransferResult> ConnectThenSend(IPEndPoint remote, Memory<byte> data, CancellationToken cancellationToken)
        {
            var error = await Network.ConnectAsync(Handle, remote, cancellationToken).ConfigureAwait(false);
            return error != SocketError.Success || data.IsEmpty
                ? new NetworkTransferResult(error, 0)
                : await Network.SendAsync(Handle, data, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<(SocketError Error, long Sent)> SendFileAsync(SafeFileHandle fileHandle, long offset, long count,
            CancellationToken cancellationToken)
        {
            long sent = 0;
            var chunk = new byte[(int)Math.Min(count, 65536)];
            while (sent < count)
            {
                var read = ReadFile(fileHandle, chunk.AsSpan(0, (int)Math.Min(chunk.Length, count - sent)), offset + sent);
                if (read == 0)
                {
                    break;
                }

                var result = await Network.SendAsync(Handle, chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sent += result.BytesTransferred;
                if (result.Error != SocketError.Success)
                {
                    return (result.Error, sent);
                }
            }

            return (SocketError.Success, sent);
        }

        // The operation under a token that also fires when the socket closes.
        private async ValueTask<T> Abortable<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken)
        {
            _abort ??= new CancellationTokenSource();
            if (!cancellationToken.CanBeCanceled)
            {
                return await operation(_abort.Token).ConfigureAwait(false);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
            return await operation(linked.Token).ConfigureAwait(false);
        }

        // Returns true with the result when the operation has completed; otherwise arranges for
        // onCompleted to receive it and returns false.
        private bool TryComplete<T>(ValueTask<T> operation, out T result, Action<T> onCompleted)
        {
            if (operation.IsCompleted)
            {
                result = operation.Result;
                return true;
            }

            _pending++;
            var awaiter = operation.GetAwaiter();
            awaiter.UnsafeOnCompleted(() =>
            {
                _pending--;
                onCompleted(awaiter.GetResult());
            });
            result = default!;
            return false;
        }

        private SocketError Transfer(ValueTask<NetworkTransferResult> operation, out int bytesTransferred,
            Action<NetworkTransferResult> callback, Action<NetworkTransferResult>? onSynchronous = null)
        {
            if (TryComplete(operation, out var result, callback))
            {
                onSynchronous?.Invoke(result);
                bytesTransferred = result.BytesTransferred;
                return result.Error;
            }

            bytesTransferred = 0;
            return SocketError.IOPending;
        }

        private SocketError Transfer(ValueTask<NetworkTransferResult> operation, out int bytesTransferred,
            Action<NetworkTransferResult> callback, Func<NetworkTransferResult, int> onSynchronous, out int addressLength)
        {
            addressLength = 0;
            if (TryComplete(operation, out var result, callback))
            {
                addressLength = onSynchronous(result);
                bytesTransferred = result.BytesTransferred;
                return result.Error;
            }

            bytesTransferred = 0;
            return SocketError.IOPending;
        }

        private SocketError Accepted(NetworkAcceptResult result, Memory<byte> socketAddress, out int socketAddressLen, out IntPtr acceptedFd)
        {
            (socketAddressLen, acceptedFd) = (0, (IntPtr)(-1));
            if (result.Error == SocketError.Success)
            {
                acceptedFd = (IntPtr)result.Socket;
                Interop.Sys.AdoptAccepted(_socket.DangerousGetHandle(), acceptedFd);
                socketAddressLen = Serialize(result.RemoteEndPoint, socketAddress.Span);
            }

            return result.Error;
        }

        // A file read at the offset, as SendFile's pread does.
        private static unsafe int ReadFile(SafeFileHandle fileHandle, Span<byte> buffer, long offset)
        {
            fixed (byte* pointer = buffer)
            {
                var read = Interop.Sys.PRead(fileHandle, pointer, buffer.Length, offset);
                if (read < 0)
                {
                    throw Interop.GetExceptionForIoErrno(Interop.Sys.GetLastErrorInfo());
                }

                return read;
            }
        }

        private static IPEndPoint EndPoint(Memory<byte> socketAddress) => IPEndPointExtensions.CreateIPEndPoint(socketAddress.Span);

        private static IPEndPoint? OptionalEndPoint(Memory<byte> socketAddress) => socketAddress.IsEmpty ? null : EndPoint(socketAddress);

        // Writes the address in SocketAddressPal's layout and returns its length.
        internal static int Serialize(IPEndPoint? endPoint, Span<byte> socketAddress)
        {
            if (endPoint is null)
            {
                return 0;
            }

            var size = endPoint.AddressFamily == AddressFamily.InterNetworkV6
                ? SocketAddressPal.IPv6AddressSize
                : SocketAddressPal.IPv4AddressSize;
            Span<byte> buffer = stackalloc byte[SocketAddressPal.IPv6AddressSize];
            buffer.Clear();
            endPoint.Serialize(buffer);
            var length = Math.Min(size, socketAddress.Length);
            buffer[..length].CopyTo(socketAddress);
            return length;
        }

        private static int Length(IList<ArraySegment<byte>> buffers)
        {
            var total = 0;
            foreach (var segment in buffers)
            {
                total += segment.Count;
            }

            return total;
        }

        private static byte[] Gather(IList<ArraySegment<byte>> buffers)
        {
            var data = new byte[Length(buffers)];
            var offset = 0;
            foreach (var segment in buffers)
            {
                segment.AsSpan().CopyTo(data.AsSpan(offset));
                offset += segment.Count;
            }

            return data;
        }

        private static void Scatter(ReadOnlySpan<byte> data, IList<ArraySegment<byte>> buffers)
        {
            foreach (var segment in buffers)
            {
                var take = Math.Min(segment.Count, data.Length);
                data[..take].CopyTo(segment.AsSpan());
                data = data[take..];
            }
        }
    }

    // Upstream's epoll engine; the network completes operations on the program's event loop,
    // so continuations are never run inline on an engine thread.
    internal static class SocketAsyncEngine
    {
        public const bool InlineSocketCompletionsEnabled = false;

        public static void OnInlineCompletionsOverride()
        {
        }
    }
}
