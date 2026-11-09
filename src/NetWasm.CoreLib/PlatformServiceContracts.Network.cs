namespace System.Runtime.InteropServices.WebAssembly
{
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    // The network facade's types. INetwork carries the TCP and UDP sockets and the name resolution
    // behind Socket, TcpClient, TcpListener, UdpClient, NetworkStream and Dns. A socket is the value
    // Open returns, from 0 to int.MaxValue. Each call reports failure as a SocketError, which the
    // socket APIs turn into the SocketException .NET throws. A blocking call returns when the
    // operation completes; its async form completes the task instead, so code that awaits never
    // blocks the program. A platform may implement only the blocking calls: the base class's async
    // forms run them.

    // An accepted connection: its socket and the peer's address, or the error.
    public readonly struct NetworkAcceptResult
    {
        public NetworkAcceptResult(SocketError error, long socket, IPEndPoint? remoteEndPoint)
        {
            Error = error;
            Socket = socket;
            RemoteEndPoint = remoteEndPoint;
        }

        public SocketError Error { get; }

        public long Socket { get; }

        public IPEndPoint? RemoteEndPoint { get; }
    }

    // How many bytes a send or receive moved, or the error; a receive of 0 bytes is the end of the
    // stream. A datagram receive also names its sender.
    public readonly struct NetworkTransferResult
    {
        public NetworkTransferResult(SocketError error, int bytesTransferred, IPEndPoint? remoteEndPoint = null)
        {
            Error = error;
            BytesTransferred = bytesTransferred;
            RemoteEndPoint = remoteEndPoint;
        }

        public SocketError Error { get; }

        public int BytesTransferred { get; }

        public IPEndPoint? RemoteEndPoint { get; }
    }

    // The addresses a host name resolves to, or the error.
    public readonly struct NetworkResolveResult
    {
        public NetworkResolveResult(SocketError error, IPAddress[] addresses)
        {
            Error = error;
            Addresses = addresses;
        }

        public SocketError Error { get; }

        public IPAddress[] Addresses { get; }
    }

    public interface INetwork
    {
        // A TCP (Stream) or UDP (Dgram) socket of the family.
        SocketError Open(AddressFamily family, SocketType type, ProtocolType protocol, out long socket);

        void Close(long socket);

        SocketError Bind(long socket, IPEndPoint localEndPoint);

        SocketError Listen(long socket, int backlog);

        SocketError Accept(long socket, out long accepted, out IPEndPoint? remoteEndPoint);

        ValueTask<NetworkAcceptResult> AcceptAsync(long socket, CancellationToken cancellationToken);

        // A TCP socket connects; a UDP socket fixes its peer.
        SocketError Connect(long socket, IPEndPoint remoteEndPoint);

        ValueTask<SocketError> ConnectAsync(long socket, IPEndPoint remoteEndPoint, CancellationToken cancellationToken);

        // A stream send returns once all the data is sent, or with the error and how much was sent.
        SocketError Send(long socket, ReadOnlySpan<byte> data, out int bytesSent);

        ValueTask<NetworkTransferResult> SendAsync(long socket, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

        SocketError Receive(long socket, Span<byte> buffer, out int bytesReceived);

        ValueTask<NetworkTransferResult> ReceiveAsync(long socket, Memory<byte> buffer, CancellationToken cancellationToken);

        // A datagram to the address, or to the connected peer when it is null.
        SocketError SendTo(long socket, ReadOnlySpan<byte> data, IPEndPoint? remoteEndPoint, out int bytesSent);

        ValueTask<NetworkTransferResult> SendToAsync(
            long socket, ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint, CancellationToken cancellationToken);

        SocketError ReceiveFrom(long socket, Span<byte> buffer, out int bytesReceived, out IPEndPoint? remoteEndPoint);

        ValueTask<NetworkTransferResult> ReceiveFromAsync(long socket, Memory<byte> buffer, CancellationToken cancellationToken);

        SocketError Shutdown(long socket, SocketShutdown how);

        SocketError GetLocalEndPoint(long socket, out IPEndPoint? endPoint);

        SocketError GetRemoteEndPoint(long socket, out IPEndPoint? endPoint);

        // Integer-valued options: buffer sizes, keep-alive, time to live, reuse, no delay.
        SocketError GetOption(long socket, SocketOptionLevel level, SocketOptionName name, out int value);

        SocketError SetOption(long socket, SocketOptionLevel level, SocketOptionName name, int value);

        SocketError ResolveAddresses(string hostName, out IPAddress[] addresses);

        ValueTask<NetworkResolveResult> ResolveAddressesAsync(string hostName, CancellationToken cancellationToken);
    }

    // The network facade's base: every blocking call throws PlatformNotSupportedException until
    // overridden, and every async call runs its blocking call, so a platform implements only what
    // its host supports.
    public abstract class PlatformNetwork : INetwork
    {
        public virtual SocketError Open(AddressFamily family, SocketType type, ProtocolType protocol, out long socket) =>
            throw PlatformServiceErrors.Unavailable("Opening a socket", "Network");

        public virtual void Close(long socket) => throw PlatformServiceErrors.Unavailable("Closing a socket", "Network");

        public virtual SocketError Bind(long socket, IPEndPoint localEndPoint) =>
            throw PlatformServiceErrors.Unavailable("Binding a socket", "Network");

        public virtual SocketError Listen(long socket, int backlog) =>
            throw PlatformServiceErrors.Unavailable("Listening on a socket", "Network");

        public virtual SocketError Accept(long socket, out long accepted, out IPEndPoint? remoteEndPoint) =>
            throw PlatformServiceErrors.Unavailable("Accepting a connection", "Network");

        public virtual ValueTask<NetworkAcceptResult> AcceptAsync(long socket, CancellationToken cancellationToken) =>
            new(new NetworkAcceptResult(Accept(socket, out var accepted, out var remote), accepted, remote));

        public virtual SocketError Connect(long socket, IPEndPoint remoteEndPoint) =>
            throw PlatformServiceErrors.Unavailable("Connecting a socket", "Network");

        public virtual ValueTask<SocketError> ConnectAsync(long socket, IPEndPoint remoteEndPoint, CancellationToken cancellationToken) =>
            new(Connect(socket, remoteEndPoint));

        public virtual SocketError Send(long socket, ReadOnlySpan<byte> data, out int bytesSent) =>
            throw PlatformServiceErrors.Unavailable("Sending on a socket", "Network");

        public virtual ValueTask<NetworkTransferResult> SendAsync(long socket, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
            new(new NetworkTransferResult(Send(socket, data.Span, out var sent), sent));

        public virtual SocketError Receive(long socket, Span<byte> buffer, out int bytesReceived) =>
            throw PlatformServiceErrors.Unavailable("Receiving on a socket", "Network");

        public virtual ValueTask<NetworkTransferResult> ReceiveAsync(long socket, Memory<byte> buffer, CancellationToken cancellationToken) =>
            new(new NetworkTransferResult(Receive(socket, buffer.Span, out var received), received));

        public virtual SocketError SendTo(long socket, ReadOnlySpan<byte> data, IPEndPoint? remoteEndPoint, out int bytesSent) =>
            throw PlatformServiceErrors.Unavailable("Sending a datagram", "Network");

        public virtual ValueTask<NetworkTransferResult> SendToAsync(
            long socket, ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint, CancellationToken cancellationToken) =>
            new(new NetworkTransferResult(SendTo(socket, data.Span, remoteEndPoint, out var sent), sent));

        public virtual SocketError ReceiveFrom(long socket, Span<byte> buffer, out int bytesReceived, out IPEndPoint? remoteEndPoint) =>
            throw PlatformServiceErrors.Unavailable("Receiving a datagram", "Network");

        public virtual ValueTask<NetworkTransferResult> ReceiveFromAsync(long socket, Memory<byte> buffer, CancellationToken cancellationToken) =>
            new(new NetworkTransferResult(ReceiveFrom(socket, buffer.Span, out var received, out var remote), received, remote));

        public virtual SocketError Shutdown(long socket, SocketShutdown how) =>
            throw PlatformServiceErrors.Unavailable("Shutting a socket down", "Network");

        public virtual SocketError GetLocalEndPoint(long socket, out IPEndPoint? endPoint) =>
            throw PlatformServiceErrors.Unavailable("A socket's local address", "Network");

        public virtual SocketError GetRemoteEndPoint(long socket, out IPEndPoint? endPoint) =>
            throw PlatformServiceErrors.Unavailable("A socket's remote address", "Network");

        public virtual SocketError GetOption(long socket, SocketOptionLevel level, SocketOptionName name, out int value) =>
            throw PlatformServiceErrors.Unavailable("Reading a socket option", "Network");

        public virtual SocketError SetOption(long socket, SocketOptionLevel level, SocketOptionName name, int value) =>
            throw PlatformServiceErrors.Unavailable("Setting a socket option", "Network");

        public virtual SocketError ResolveAddresses(string hostName, out IPAddress[] addresses) =>
            throw PlatformServiceErrors.Unavailable("Resolving a host name", "Network");

        public virtual ValueTask<NetworkResolveResult> ResolveAddressesAsync(string hostName, CancellationToken cancellationToken) =>
            new(new NetworkResolveResult(ResolveAddresses(hostName, out var addresses), addresses));
    }
}
