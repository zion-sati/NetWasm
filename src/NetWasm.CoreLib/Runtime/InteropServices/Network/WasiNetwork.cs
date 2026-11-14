namespace System.Runtime.InteropServices.Network;

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.Concurrency;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading;
using System.Threading.Tasks;
using Imports = WasiNetworkImports;
using Layout = WasiNetworkLayout;

// One socket's wasi:sockets 0.3 state. A connected TCP socket has the stream it writes and its
// send future, and the stream it reads and its receive future; a listener has the stream its
// connections arrive on. Each direction has at most one copy in progress.
internal sealed class WasiSocket(bool stream, AddressFamily family, int resource)
{
    internal bool IsStream { get; } = stream;
    internal AddressFamily Family { get; } = family;
    internal int Resource { get; set; } = resource;
    internal bool Bound { get; set; }
    // A dual-mode IPv6 socket that took an IPv4-mapped address runs on an IPv4 host socket.
    internal bool Mapped { get; set; }
    internal int Connections { get; set; }
    internal int SendWritable { get; set; }
    internal int SendFuture { get; set; }
    internal int ReceiveReadable { get; set; }
    internal int ReceiveFuture { get; set; }
    internal bool Connected { get; set; }
    internal bool ReceiveEnded { get; set; }
    internal WasiPendingCopy? PendingRead { get; set; }
    internal WasiPendingCopy? PendingWrite { get; set; }
    internal Dictionary<(SocketOptionLevel, SocketOptionName), int> Options { get; } = [];
}

// A stream copy the event loop completes: BLOCKED becomes a task its event completes, and a
// cancellation stops waiting for the event and cancels the copy at once.
internal sealed class WasiPendingCopy
{
    private readonly TaskCompletionSource<int> _completion = new();
    private IPlatformSchedule? _tracking;

    internal Task<int> Completion => _completion.Task;

    internal static WasiPendingCopy Start(int status, int waitable)
    {
        var copy = new WasiPendingCopy();
        if (status != Layout.Blocked)
        {
            copy._completion.SetResult(status);
        }
        else if (PlatformServices.Scheduler is CommandWorldScheduler loop)
        {
            copy._tracking = loop.TrackCopy(payload => copy._completion.TrySetResult(unchecked((int)payload)), waitable);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "An asynchronous socket operation needs the Component Model's event loop, which a core module does not have.");
        }

        return copy;
    }

    internal void Cancel(Func<int> cancelCopy)
    {
        if (_completion.Task.IsCompleted)
        {
            return;
        }

        _tracking!.Dispose();
        _completion.TrySetResult(cancelCopy());
    }
}

// NetWasm's WASI 0.3 network: TCP and UDP sockets and name lookup over wasi:sockets. A blocking
// call waits for its subtask or copy; an async one completes its task from the event loop.
internal sealed class WasiNetwork : INetwork
{
    private readonly ISubtaskReturnAwaiter _subtasks;
    private readonly IWaitableEventAwaiter _copies;
    private readonly ICanonicalBufferAllocator _allocator;
    private readonly Dictionary<long, WasiSocket> _sockets = [];
    private long _next;

    internal WasiNetwork(ISubtaskReturnAwaiter subtasks, IWaitableEventAwaiter copies, ICanonicalBufferAllocator allocator)
    {
        _subtasks = subtasks ?? throw new ArgumentNullException(nameof(subtasks));
        _copies = copies ?? throw new ArgumentNullException(nameof(copies));
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
    }

    public SocketError Open(AddressFamily family, SocketType type, ProtocolType protocol, out long socket)
    {
        socket = -1;
        if (family is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return SocketError.AddressFamilyNotSupported;
        }

        var stream = type == SocketType.Stream;
        if (stream ? protocol is not (ProtocolType.Tcp or ProtocolType.Unspecified)
            : type != SocketType.Dgram || protocol is not (ProtocolType.Udp or ProtocolType.Unspecified))
        {
            return type is SocketType.Stream or SocketType.Dgram ? SocketError.ProtocolNotSupported : SocketError.SocketNotSupported;
        }

        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        if (stream)
        {
            Imports.CreateTcp(Layout.Family(family), result.Address);
        }
        else
        {
            Imports.CreateUdp(Layout.Family(family), result.Address);
        }

        var error = Layout.ReadResult(result.Address);
        if (error == SocketError.Success)
        {
            socket = _next++;
            _sockets[socket] = new WasiSocket(stream, family, CanonicalAbi.ReadInt32(result.Address, Layout.Pointer));
        }

        return error;
    }

    public void Close(long socket)
    {
        if (!_sockets.Remove(socket, out var state))
        {
            return;
        }

        state.PendingRead?.Cancel(() => state.Connections != 0
            ? Imports.CancelReadConnections(state.Connections)
            : Imports.CancelReadBytes(state.ReceiveReadable));
        state.PendingWrite?.Cancel(() => Imports.CancelWriteBytes(state.SendWritable));
        DropSend(state);
        DropReceive(state);
        if (state.Connections != 0)
        {
            Imports.DropConnections(state.Connections);
        }

        if (state.IsStream)
        {
            Imports.DropTcp(state.Resource);
        }
        else
        {
            Imports.DropUdp(state.Resource);
        }
    }

    public SocketError Bind(long socket, IPEndPoint localEndPoint)
    {
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        if (IsDualMode(state) && !state.Mapped && localEndPoint.Address.Equals(IPAddress.IPv6Any))
        {
            // WASI has no dual-stack socket that receives from both families.
            return SocketError.OperationNotSupported;
        }

        var mapping = ToHost(state, localEndPoint, out var host);
        if (mapping != SocketError.Success)
        {
            return mapping;
        }

        var flat = Layout.Flatten(host);
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        if (state.IsStream)
        {
            Imports.BindTcp(state.Resource, flat[0], flat[1], flat[2], flat[3], flat[4], flat[5], flat[6], flat[7], flat[8],
                flat[9], flat[10], flat[11], result.Address);
        }
        else
        {
            Imports.BindUdp(state.Resource, flat[0], flat[1], flat[2], flat[3], flat[4], flat[5], flat[6], flat[7], flat[8],
                flat[9], flat[10], flat[11], result.Address);
        }

        var error = Layout.ReadResult(result.Address);
        state.Bound |= error == SocketError.Success;
        return error;
    }

    public SocketError Listen(long socket, int backlog)
    {
        if (!Find(socket, out var state) || !state.IsStream)
        {
            return state is null ? SocketError.NotSocket : SocketError.OperationNotSupported;
        }

        if (state.Connections != 0)
        {
            return SocketError.Success;
        }

        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        if (backlog > 0)
        {
            // A host that does not take the size keeps its own.
            Imports.SetListenBacklog(state.Resource, backlog, result.Address);
        }

        Imports.Listen(state.Resource, result.Address);
        var error = Layout.ReadResult(result.Address);
        if (error == SocketError.Success)
        {
            state.Connections = CanonicalAbi.ReadInt32(result.Address, Layout.Pointer);
        }

        return error;
    }

    public SocketError Accept(long socket, out long accepted, out IPEndPoint? remoteEndPoint)
    {
        accepted = -1;
        remoteEndPoint = null;
        if (!Listening(socket, out var state, out var refusal))
        {
            return refusal;
        }

        using var buffer = _allocator.Allocate(4, 4);
        var status = Imports.ReadConnections(state.Connections, buffer.Address, 1);
        var progress = status == Layout.Blocked ? unchecked((int)_copies.Await(state.Connections)) : status;
        return Accepted(state, progress, buffer.Address, out accepted, out remoteEndPoint);
    }

    public async ValueTask<NetworkAcceptResult> AcceptAsync(long socket, CancellationToken cancellationToken)
    {
        if (!Listening(socket, out var state, out var refusal))
        {
            return new NetworkAcceptResult(refusal, -1, null);
        }

        var buffer = _allocator.Allocate(4, 4);
        try
        {
            var copy = WasiPendingCopy.Start(Imports.ReadConnections(state.Connections, buffer.Address, 1), state.Connections);
            state.PendingRead = copy;
            int progress;
            using (cancellationToken.Register(() => copy.Cancel(() => Imports.CancelReadConnections(state.Connections))))
            {
                progress = await copy.Completion.ConfigureAwait(false);
            }

            state.PendingRead = null;
            var error = Accepted(state, progress, buffer.Address, out var accepted, out var remote);
            return new NetworkAcceptResult(error, accepted, remote);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    public SocketError Connect(long socket, IPEndPoint remoteEndPoint)
    {
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        if (!state.IsStream)
        {
            return ConnectDatagram(state, remoteEndPoint);
        }

        if (state.Connected)
        {
            return SocketError.IsConnected;
        }

        var mapping = ToHost(state, remoteEndPoint, out var host);
        if (mapping != SocketError.Success)
        {
            return mapping;
        }

        using var parameters = _allocator.Allocate(4 + Layout.SocketAddressSize, 4);
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        CanonicalAbi.WriteInt32(parameters.Address, 0, state.Resource);
        Layout.WriteAddress(parameters.Address + 4, host);
        _subtasks.AwaitReturn(Imports.ConnectTcp(parameters.Address, result.Address));
        return Connected(state, Layout.ReadResult(result.Address));
    }

    public async ValueTask<SocketError> ConnectAsync(long socket, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        if (!state.IsStream)
        {
            return ConnectDatagram(state, remoteEndPoint);
        }

        if (state.Connected)
        {
            return SocketError.IsConnected;
        }

        var mapping = ToHost(state, remoteEndPoint, out var host);
        if (mapping != SocketError.Success)
        {
            return mapping;
        }

        var parameters = _allocator.Allocate(4 + Layout.SocketAddressSize, 4);
        var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        try
        {
            CanonicalAbi.WriteInt32(parameters.Address, 0, state.Resource);
            Layout.WriteAddress(parameters.Address + 4, host);
            await PlatformServices.AwaitSubtask(Imports.ConnectTcp(parameters.Address, result.Address)).ConfigureAwait(false);
            return Connected(state, Layout.ReadResult(result.Address));
        }
        finally
        {
            parameters.Dispose();
            result.Dispose();
        }
    }

    public SocketError Send(long socket, ReadOnlySpan<byte> data, out int bytesSent)
    {
        bytesSent = 0;
        if (!Writable(socket, out var state, out var refusal))
        {
            return refusal;
        }

        using var buffer = Copy(data);
        nuint written = 0;
        var length = unchecked((nuint)data.Length);
        while (written < length)
        {
            var status = Imports.WriteBytes(state.SendWritable, buffer.Address + written, length - written);
            var progress = status == Layout.Blocked ? _copies.Await(state.SendWritable) : unchecked((uint)status);
            written += progress >> 4;
            if ((progress & 0xF) != Layout.Completed)
            {
                bytesSent = unchecked((int)written);
                return SendFailure(state);
            }
        }

        bytesSent = data.Length;
        return SocketError.Success;
    }

    // A copy that would wait is cancelled at once; whatever the host took before the cancel counts.
    public SocketError TrySend(long socket, ReadOnlySpan<byte> data, out int bytesSent)
    {
        bytesSent = 0;
        if (!Writable(socket, out var state, out var refusal))
        {
            return refusal;
        }

        if (data.IsEmpty)
        {
            return SocketError.Success;
        }

        using var buffer = Copy(data);
        nuint written = 0;
        var length = unchecked((nuint)data.Length);
        while (written < length)
        {
            var status = Imports.WriteBytes(state.SendWritable, buffer.Address + written, length - written);
            var progress = status == Layout.Blocked
                ? unchecked((uint)Imports.CancelWriteBytes(state.SendWritable))
                : unchecked((uint)status);
            written += progress >> 4;
            if ((progress & 0xF) == Layout.Cancelled)
            {
                break;
            }

            if ((progress & 0xF) != Layout.Completed)
            {
                bytesSent = unchecked((int)written);
                return SendFailure(state);
            }
        }

        bytesSent = unchecked((int)written);
        return written == 0 ? SocketError.WouldBlock : SocketError.Success;
    }

    public async ValueTask<NetworkTransferResult> SendAsync(long socket, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!Writable(socket, out var state, out var refusal))
        {
            return new NetworkTransferResult(refusal, 0);
        }

        var buffer = Copy(data.Span);
        try
        {
            nuint written = 0;
            var length = unchecked((nuint)data.Length);
            while (written < length)
            {
                var copy = WasiPendingCopy.Start(
                    Imports.WriteBytes(state.SendWritable, buffer.Address + written, length - written), state.SendWritable);
                state.PendingWrite = copy;
                uint progress;
                using (cancellationToken.Register(() => copy.Cancel(() => Imports.CancelWriteBytes(state.SendWritable))))
                {
                    progress = unchecked((uint)await copy.Completion.ConfigureAwait(false));
                }

                state.PendingWrite = null;
                written += progress >> 4;
                if ((progress & 0xF) != Layout.Completed)
                {
                    var error = (progress & 0xF) == 2 ? SocketError.OperationAborted : SendFailure(state);
                    return new NetworkTransferResult(error, unchecked((int)written));
                }
            }

            return new NetworkTransferResult(SocketError.Success, data.Length);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    public SocketError Receive(long socket, Span<byte> buffer, out int bytesReceived)
    {
        bytesReceived = 0;
        if (!Readable(socket, out var state, out var refusal))
        {
            return refusal;
        }

        if (state.ReceiveEnded || buffer.IsEmpty)
        {
            return SocketError.Success;
        }

        var length = unchecked((nuint)buffer.Length);
        using var copy = _allocator.Allocate(length, 1);
        uint progress;
        do
        {
            var status = Imports.ReadBytes(state.ReceiveReadable, copy.Address, length);
            progress = status == Layout.Blocked ? _copies.Await(state.ReceiveReadable) : unchecked((uint)status);
        }
        while (progress == Layout.Completed);

        return Received(state, progress, copy.Address, buffer, out bytesReceived);
    }

    // A read that would wait is cancelled at once; bytes that arrived before the cancel are returned.
    public SocketError TryReceive(long socket, Span<byte> buffer, out int bytesReceived)
    {
        bytesReceived = 0;
        if (!Readable(socket, out var state, out var refusal))
        {
            return refusal;
        }

        if (state.ReceiveEnded || buffer.IsEmpty)
        {
            return SocketError.Success;
        }

        var length = unchecked((nuint)buffer.Length);
        using var copy = _allocator.Allocate(length, 1);
        uint progress;
        do
        {
            var status = Imports.ReadBytes(state.ReceiveReadable, copy.Address, length);
            progress = status == Layout.Blocked
                ? unchecked((uint)Imports.CancelReadBytes(state.ReceiveReadable))
                : unchecked((uint)status);
            if (progress == Layout.Cancelled)
            {
                return SocketError.WouldBlock;
            }
        }
        while (progress == Layout.Completed);

        return Received(state, progress, copy.Address, buffer, out bytesReceived);
    }

    public async ValueTask<NetworkTransferResult> ReceiveAsync(long socket, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!Readable(socket, out var state, out var refusal))
        {
            return new NetworkTransferResult(refusal, 0);
        }

        if (state.ReceiveEnded || buffer.IsEmpty)
        {
            return new NetworkTransferResult(SocketError.Success, 0);
        }

        var length = unchecked((nuint)buffer.Length);
        var copied = _allocator.Allocate(length, 1);
        try
        {
            uint progress;
            do
            {
                var copy = WasiPendingCopy.Start(Imports.ReadBytes(state.ReceiveReadable, copied.Address, length), state.ReceiveReadable);
                state.PendingRead = copy;
                using (cancellationToken.Register(() => copy.Cancel(() => Imports.CancelReadBytes(state.ReceiveReadable))))
                {
                    progress = unchecked((uint)await copy.Completion.ConfigureAwait(false));
                }

                state.PendingRead = null;
            }
            while (progress == Layout.Completed);

            if ((progress & 0xF) == 2 && progress >> 4 == 0)
            {
                return new NetworkTransferResult(SocketError.OperationAborted, 0);
            }

            var error = Received(state, progress, copied.Address, buffer.Span, out var received);
            return new NetworkTransferResult(error, received);
        }
        finally
        {
            copied.Dispose();
        }
    }

    public SocketError SendTo(long socket, ReadOnlySpan<byte> data, IPEndPoint? remoteEndPoint, out int bytesSent)
    {
        bytesSent = 0;
        if (!Datagram(socket, out var state, out var refusal))
        {
            return refusal;
        }

        var mapping = ToHostOptional(state, remoteEndPoint, out var host);
        if (mapping != SocketError.Success)
        {
            return mapping;
        }

        using var bytes = Copy(data);
        using var parameters = DatagramParameters(state, bytes, data.Length, host);
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        _subtasks.AwaitReturn(Imports.SendDatagram(parameters.Address, result.Address));
        var error = Layout.ReadResult(result.Address);
        bytesSent = error == SocketError.Success ? data.Length : 0;
        return error;
    }

    public async ValueTask<NetworkTransferResult> SendToAsync(
        long socket, ReadOnlyMemory<byte> data, IPEndPoint? remoteEndPoint, CancellationToken cancellationToken)
    {
        if (!Datagram(socket, out var state, out var refusal))
        {
            return new NetworkTransferResult(refusal, 0);
        }

        var mapping = ToHostOptional(state, remoteEndPoint, out var host);
        if (mapping != SocketError.Success)
        {
            return new NetworkTransferResult(mapping, 0);
        }

        var bytes = Copy(data.Span);
        var parameters = DatagramParameters(state, bytes, data.Length, host);
        var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        try
        {
            await PlatformServices.AwaitSubtask(Imports.SendDatagram(parameters.Address, result.Address)).ConfigureAwait(false);
            var error = Layout.ReadResult(result.Address);
            return new NetworkTransferResult(error, error == SocketError.Success ? data.Length : 0);
        }
        finally
        {
            bytes.Dispose();
            parameters.Dispose();
            result.Dispose();
        }
    }

    public SocketError ReceiveFrom(long socket, Span<byte> buffer, out int bytesReceived, out IPEndPoint? remoteEndPoint)
    {
        bytesReceived = 0;
        remoteEndPoint = null;
        if (!Datagram(socket, out var state, out var refusal))
        {
            return refusal;
        }

        using var result = _allocator.Allocate(DatagramResultSize, Layout.Pointer);
        _subtasks.AwaitReturn(Imports.ReceiveDatagram(state.Resource, result.Address));
        var error = Datagram(result.Address, buffer, out bytesReceived, out var remote);
        remoteEndPoint = FromHost(state, remote);
        return error;
    }

    public async ValueTask<NetworkTransferResult> ReceiveFromAsync(long socket, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!Datagram(socket, out var state, out var refusal))
        {
            return new NetworkTransferResult(refusal, 0);
        }

        var result = _allocator.Allocate(DatagramResultSize, Layout.Pointer);
        try
        {
            await PlatformServices.AwaitSubtask(Imports.ReceiveDatagram(state.Resource, result.Address)).ConfigureAwait(false);
            var error = Datagram(result.Address, buffer.Span, out var received, out var remote);
            return new NetworkTransferResult(error, received, FromHost(state, remote));
        }
        finally
        {
            result.Dispose();
        }
    }

    // TCP has no shutdown call: ending the stream it writes, or the one it reads, ends that
    // direction.
    public SocketError Shutdown(long socket, SocketShutdown how)
    {
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        if (!state.Connected)
        {
            return SocketError.NotConnected;
        }

        if (state.IsStream && how != SocketShutdown.Receive)
        {
            state.PendingWrite?.Cancel(() => Imports.CancelWriteBytes(state.SendWritable));
            DropSend(state);
        }

        if (state.IsStream && how != SocketShutdown.Send)
        {
            state.PendingRead?.Cancel(() => Imports.CancelReadBytes(state.ReceiveReadable));
            DropReceive(state);
        }

        return SocketError.Success;
    }

    public SocketError GetLocalEndPoint(long socket, out IPEndPoint? endPoint) => EndPoint(socket, remote: false, out endPoint);

    public SocketError GetRemoteEndPoint(long socket, out IPEndPoint? endPoint) => EndPoint(socket, remote: true, out endPoint);

    public SocketError GetOption(long socket, SocketOptionLevel level, SocketOptionName name, out int value)
    {
        value = 0;
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        switch (Option(state, level, name))
        {
            case HostOption.ReceiveBuffer or HostOption.SendBuffer:
                {
                    using var result = _allocator.Allocate(8 + Layout.Pointer * 4, 8);
                    var receive = Option(state, level, name) == HostOption.ReceiveBuffer;
                    if (state.IsStream)
                    {
                        if (receive) Imports.GetTcpReceiveBuffer(state.Resource, result.Address);
                        else Imports.GetTcpSendBuffer(state.Resource, result.Address);
                    }
                    else if (receive)
                    {
                        Imports.GetUdpReceiveBuffer(state.Resource, result.Address);
                    }
                    else
                    {
                        Imports.GetUdpSendBuffer(state.Resource, result.Address);
                    }

                    return ReadOption(result.Address, 8, out value, width: 8);
                }

            case HostOption.KeepAlive:
                {
                    using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
                    Imports.GetKeepAlive(state.Resource, result.Address);
                    return ReadOption(result.Address, Layout.Pointer, out value, width: 1);
                }

            case HostOption.HopLimit:
                {
                    using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
                    if (state.IsStream) Imports.GetTcpHopLimit(state.Resource, result.Address);
                    else Imports.GetUdpHopLimit(state.Resource, result.Address);
                    return ReadOption(result.Address, Layout.Pointer, out value, width: 1);
                }

            case HostOption.Kept:
                value = state.Options.TryGetValue((level, name), out var kept) ? kept
                    : name == SocketOptionName.IPv6Only && state.Family == AddressFamily.InterNetworkV6 ? 1 : 0;
                return SocketError.Success;

            default:
                return SocketError.ProtocolOption;
        }
    }

    public SocketError SetOption(long socket, SocketOptionLevel level, SocketOptionName name, int value)
    {
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        var option = Option(state, level, name);
        if (option == HostOption.None)
        {
            return SocketError.ProtocolOption;
        }

        if (option == HostOption.Kept)
        {
            if (name == SocketOptionName.IPv6Only && (state.Bound || state.Connected || state.Mapped))
            {
                return SocketError.InvalidArgument;
            }

            state.Options[(level, name)] = value;
            return SocketError.Success;
        }

        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        switch (option)
        {
            case HostOption.ReceiveBuffer when state.IsStream: Imports.SetTcpReceiveBuffer(state.Resource, value, result.Address); break;
            case HostOption.ReceiveBuffer: Imports.SetUdpReceiveBuffer(state.Resource, value, result.Address); break;
            case HostOption.SendBuffer when state.IsStream: Imports.SetTcpSendBuffer(state.Resource, value, result.Address); break;
            case HostOption.SendBuffer: Imports.SetUdpSendBuffer(state.Resource, value, result.Address); break;
            case HostOption.KeepAlive: Imports.SetKeepAlive(state.Resource, value != 0 ? 1 : 0, result.Address); break;
            case HostOption.HopLimit when state.IsStream: Imports.SetTcpHopLimit(state.Resource, value, result.Address); break;
            default: Imports.SetUdpHopLimit(state.Resource, value, result.Address); break;
        }

        return Layout.ReadResult(result.Address);
    }

    public SocketError ResolveAddresses(string hostName, out IPAddress[] addresses)
    {
        ArgumentNullException.ThrowIfNull(hostName);
        using var name = CanonicalAbi.LowerString(hostName);
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        _subtasks.AwaitReturn(Imports.ResolveAddresses(name.Address, name.Length, result.Address));
        return Resolved(result.Address, out addresses);
    }

    public async ValueTask<NetworkResolveResult> ResolveAddressesAsync(string hostName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostName);
        var name = CanonicalAbi.LowerString(hostName);
        var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        try
        {
            await PlatformServices.AwaitSubtask(Imports.ResolveAddresses(name.Address, name.Length, result.Address)).ConfigureAwait(false);
            var error = Resolved(result.Address, out var addresses);
            return new NetworkResolveResult(error, addresses);
        }
        finally
        {
            name.Dispose();
            result.Dispose();
        }
    }

    // result<tuple<list<u8>, ip-socket-address>, error-code>: the list at P, the address at 3P.
    private static nuint DatagramResultSize => Layout.Pointer * 3 + Layout.SocketAddressSize;

    private enum HostOption
    {
        None,
        ReceiveBuffer,
        SendBuffer,
        KeepAlive,
        HopLimit,
        // Options WASI does not expose: kept as set, as a host that ignores them would.
        Kept,
    }

    private static HostOption Option(WasiSocket state, SocketOptionLevel level, SocketOptionName name) => (level, name) switch
    {
        (SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer) => HostOption.ReceiveBuffer,
        (SocketOptionLevel.Socket, SocketOptionName.SendBuffer) => HostOption.SendBuffer,
        (SocketOptionLevel.Socket, SocketOptionName.KeepAlive) when state.IsStream => HostOption.KeepAlive,
        (SocketOptionLevel.IP, SocketOptionName.IpTimeToLive) or (SocketOptionLevel.IPv6, SocketOptionName.HopLimit) =>
            HostOption.HopLimit,
        (SocketOptionLevel.Socket, SocketOptionName.ReuseAddress or SocketOptionName.ExclusiveAddressUse
            or SocketOptionName.Linger or SocketOptionName.DontLinger or SocketOptionName.Broadcast)
            or (SocketOptionLevel.Tcp, SocketOptionName.NoDelay)
            or (SocketOptionLevel.IPv6, SocketOptionName.IPv6Only) => HostOption.Kept,
        _ => HostOption.None,
    };

    private static SocketError ReadOption(nuint result, nuint payload, out int value, int width)
    {
        var error = Layout.ReadResult(result);
        value = error != SocketError.Success ? 0
            : width == 8 ? unchecked((int)Math.Min(CanonicalAbi.ReadInt64(result, payload), int.MaxValue))
            : CanonicalAbi.ReadByte(result, payload);
        return error;
    }

    private bool Find(long socket, out WasiSocket state) => _sockets.TryGetValue(socket, out state!);

    private bool Listening(long socket, out WasiSocket state, out SocketError refusal)
    {
        refusal = !Find(socket, out state) ? SocketError.NotSocket
            : state.Connections == 0 ? SocketError.InvalidArgument
            : state.PendingRead is not null ? SocketError.AlreadyInProgress
            : SocketError.Success;
        return refusal == SocketError.Success;
    }

    private bool Writable(long socket, out WasiSocket state, out SocketError refusal)
    {
        refusal = !Find(socket, out state) ? SocketError.NotSocket
            : !state.IsStream ? SocketError.OperationNotSupported
            : !state.Connected ? SocketError.NotConnected
            : state.SendWritable == 0 ? SocketError.Shutdown
            : state.PendingWrite is not null ? SocketError.AlreadyInProgress
            : SocketError.Success;
        return refusal == SocketError.Success;
    }

    private bool Readable(long socket, out WasiSocket state, out SocketError refusal)
    {
        refusal = !Find(socket, out state) ? SocketError.NotSocket
            : !state.IsStream ? SocketError.OperationNotSupported
            : !state.Connected ? SocketError.NotConnected
            : state.PendingRead is not null ? SocketError.AlreadyInProgress
            : SocketError.Success;
        return refusal == SocketError.Success;
    }

    private bool Datagram(long socket, out WasiSocket state, out SocketError refusal)
    {
        refusal = !Find(socket, out state) ? SocketError.NotSocket
            : state.IsStream ? SocketError.OperationNotSupported
            : SocketError.Success;
        return refusal == SocketError.Success;
    }

    private SocketError ConnectDatagram(WasiSocket state, IPEndPoint remoteEndPoint)
    {
        var mapping = ToHost(state, remoteEndPoint, out var host);
        if (mapping != SocketError.Success)
        {
            return mapping;
        }

        var flat = Layout.Flatten(host);
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        Imports.ConnectUdp(state.Resource, flat[0], flat[1], flat[2], flat[3], flat[4], flat[5], flat[6], flat[7], flat[8],
            flat[9], flat[10], flat[11], result.Address);
        var error = Layout.ReadResult(result.Address);
        state.Connected |= error == SocketError.Success;
        return error;
    }

    private SocketError Accepted(WasiSocket listener, int progress, nuint buffer, out long accepted, out IPEndPoint? remote)
    {
        accepted = -1;
        remote = null;
        if (progress >> 4 == 0)
        {
            return (progress & 0xF) == 2 ? SocketError.OperationAborted : SocketError.ConnectionAborted;
        }

        var connection = new WasiSocket(stream: true, listener.Family, CanonicalAbi.ReadInt32(buffer, 0)) { Mapped = listener.Mapped };
        accepted = _next++;
        _sockets[accepted] = connection;
        Connected(connection, SocketError.Success);
        EndPoint(accepted, remote: true, out remote);
        return SocketError.Success;
    }

    // A connected TCP socket hands the host the stream it writes and takes the one it reads.
    private SocketError Connected(WasiSocket state, SocketError error)
    {
        if (error != SocketError.Success)
        {
            return error;
        }

        var ends = Imports.CreateSendStream();
        state.SendWritable = unchecked((int)(ends >> 32));
        state.SendFuture = Imports.Send(state.Resource, unchecked((int)ends));
        using (var received = _allocator.Allocate(8, 4))
        {
            Imports.Receive(state.Resource, received.Address);
            state.ReceiveReadable = CanonicalAbi.ReadInt32(received.Address, 0);
            state.ReceiveFuture = CanonicalAbi.ReadInt32(received.Address, 4);
        }

        state.Connected = true;
        return SocketError.Success;
    }

    // The host stopped taking bytes: its send future says why.
    private SocketError SendFailure(WasiSocket state)
    {
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        var status = Imports.ReadSendResult(state.SendFuture, result.Address);
        if (status == Layout.Blocked)
        {
            _copies.Await(state.SendFuture);
        }

        var error = Layout.ReadResult(result.Address);
        DropSend(state);
        return error == SocketError.Success ? SocketError.ConnectionReset : error;
    }

    // A read's bytes, or the end of the stream with its receive future's verdict.
    private SocketError Received(WasiSocket state, uint progress, nuint copied, Span<byte> buffer, out int bytesReceived)
    {
        bytesReceived = unchecked((int)(progress >> 4));
        for (var index = 0; index < bytesReceived; index++)
        {
            buffer[index] = CanonicalAbi.ReadByte(copied, unchecked((nuint)index));
        }

        if (bytesReceived != 0 || (progress & 0xF) != Layout.Dropped)
        {
            return SocketError.Success;
        }

        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        var status = Imports.ReadReceiveResult(state.ReceiveFuture, result.Address);
        if (status == Layout.Blocked)
        {
            _copies.Await(state.ReceiveFuture);
        }

        var error = Layout.ReadResult(result.Address);
        DropReceive(state);
        return error;
    }

    private CanonicalBuffer DatagramParameters(WasiSocket state, CanonicalBuffer bytes, int length, IPEndPoint? remote)
    {
        var pointer = Layout.Pointer;
        var parameters = _allocator.Allocate(pointer * 3 + 4 + Layout.SocketAddressSize, pointer);
        CanonicalAbi.WriteInt32(parameters.Address, 0, state.Resource);
        CanonicalAbi.WriteAddress(parameters.Address, pointer, bytes.Address);
        CanonicalAbi.WriteAddress(parameters.Address, pointer * 2, unchecked((nuint)length));
        CanonicalAbi.WriteByte(parameters.Address, pointer * 3, remote is null ? (byte)0 : (byte)1);
        if (remote is not null)
        {
            Layout.WriteAddress(parameters.Address + pointer * 3 + 4, remote);
        }

        return parameters;
    }

    private static SocketError Datagram(nuint result, Span<byte> buffer, out int bytesReceived, out IPEndPoint? remote)
    {
        bytesReceived = 0;
        remote = null;
        var error = Layout.ReadResult(result);
        if (error != SocketError.Success)
        {
            return error;
        }

        var pointer = Layout.Pointer;
        var data = CanonicalAbi.ReadAddress(result, pointer);
        var length = unchecked((int)CanonicalAbi.ReadAddress(result, pointer * 2));
        remote = Layout.ReadAddress(result + pointer * 3);
        bytesReceived = Math.Min(length, buffer.Length);
        for (var index = 0; index < bytesReceived; index++)
        {
            buffer[index] = CanonicalAbi.ReadByte(data, unchecked((nuint)index));
        }

        if (length != 0)
        {
            CanonicalAbi.Free(data);
        }

        return length > buffer.Length ? SocketError.MessageSize : SocketError.Success;
    }

    private static SocketError Resolved(nuint result, out IPAddress[] addresses)
    {
        addresses = [];
        if (CanonicalAbi.ReadByte(result, 0) != 0)
        {
            return Layout.ReadLookupError(result + Layout.Pointer);
        }

        var list = CanonicalAbi.ReadAddress(result, Layout.Pointer);
        var count = unchecked((int)CanonicalAbi.ReadAddress(result, Layout.Pointer * 2));
        addresses = new IPAddress[count];
        for (var index = 0; index < count; index++)
        {
            addresses[index] = Layout.ReadIPAddress(list + Layout.IPAddressSize * unchecked((nuint)index));
        }

        if (count != 0)
        {
            CanonicalAbi.Free(list);
        }

        return SocketError.Success;
    }

    private SocketError EndPoint(long socket, bool remote, out IPEndPoint? endPoint)
    {
        endPoint = null;
        if (!Find(socket, out var state))
        {
            return SocketError.NotSocket;
        }

        using var result = _allocator.Allocate(Layout.AddressResultSize + Layout.Pointer * 4, Layout.Pointer);
        switch ((state.IsStream, remote))
        {
            case (true, true): Imports.TcpRemoteAddress(state.Resource, result.Address); break;
            case (true, false): Imports.TcpLocalAddress(state.Resource, result.Address); break;
            case (false, true): Imports.UdpRemoteAddress(state.Resource, result.Address); break;
            default: Imports.UdpLocalAddress(state.Resource, result.Address); break;
        }

        var error = Layout.ReadResult(result.Address);
        if (error == SocketError.Success)
        {
            endPoint = FromHost(state, Layout.ReadAddress(result.Address + Layout.Pointer));
        }

        return error;
    }

    // IPV6_V6ONLY off. WASI's IPv6 sockets are always IPv6-only, so a dual-mode socket given an
    // IPv4-mapped address before it binds or connects is re-created as an IPv4 host socket and
    // reports its addresses mapped back, as a Linux dual-mode socket reports them. Binding one to
    // the IPv6 wildcard, which would receive from both families, is not supported.
    private static bool IsDualMode(WasiSocket state) =>
        state.Family == AddressFamily.InterNetworkV6 &&
        state.Options.TryGetValue((SocketOptionLevel.IPv6, SocketOptionName.IPv6Only), out var v6Only) && v6Only == 0;

    // The endpoint the host socket takes for the one the program gave.
    // A datagram's optional destination.
    private SocketError ToHostOptional(WasiSocket state, IPEndPoint? endPoint, out IPEndPoint? host)
    {
        host = null;
        if (endPoint is null)
        {
            return SocketError.Success;
        }

        var error = ToHost(state, endPoint, out var required);
        host = required;
        return error;
    }

    private SocketError ToHost(WasiSocket state, IPEndPoint endPoint, out IPEndPoint host)
    {
        host = endPoint;
        if (!(state.Mapped || IsDualMode(state)) ||
            (!state.Mapped && !endPoint.Address.IsIPv4MappedToIPv6))
        {
            return SocketError.Success;
        }

        if (!endPoint.Address.IsIPv4MappedToIPv6)
        {
            // An IPv4 host socket cannot reach an IPv6 address.
            return SocketError.AddressFamilyNotSupported;
        }

        if (!state.Mapped)
        {
            if (state.Bound || state.Connected)
            {
                return SocketError.InvalidArgument;
            }

            var error = Recreate(state, AddressFamily.InterNetwork);
            if (error != SocketError.Success)
            {
                return error;
            }

            state.Mapped = true;
        }

        host = new IPEndPoint(endPoint.Address.MapToIPv4(), endPoint.Port);
        return SocketError.Success;
    }

    private static IPEndPoint? FromHost(WasiSocket state, IPEndPoint? endPoint) =>
        state.Mapped && endPoint is not null ? new IPEndPoint(endPoint.Address.MapToIPv6(), endPoint.Port) : endPoint;

    // Replaces the socket's unbound host socket with one of the family.
    private SocketError Recreate(WasiSocket state, AddressFamily family)
    {
        using var result = _allocator.Allocate(Layout.SmallResultSize, Layout.Pointer);
        if (state.IsStream)
        {
            Imports.CreateTcp(Layout.Family(family), result.Address);
        }
        else
        {
            Imports.CreateUdp(Layout.Family(family), result.Address);
        }

        var error = Layout.ReadResult(result.Address);
        if (error == SocketError.Success)
        {
            if (state.IsStream)
            {
                Imports.DropTcp(state.Resource);
            }
            else
            {
                Imports.DropUdp(state.Resource);
            }

            state.Resource = CanonicalAbi.ReadInt32(result.Address, Layout.Pointer);
        }

        return error;
    }

    private CanonicalBuffer Copy(ReadOnlySpan<byte> data)
    {
        var buffer = _allocator.Allocate(unchecked((nuint)Math.Max(data.Length, 1)), 1);
        for (var index = 0; index < data.Length; index++)
        {
            CanonicalAbi.WriteByte(buffer.Address, unchecked((nuint)index), data[index]);
        }

        return buffer;
    }

    private static void DropSend(WasiSocket state)
    {
        if (state.SendWritable != 0)
        {
            Imports.DropSendStream(state.SendWritable);
            state.SendWritable = 0;
        }

        if (state.SendFuture != 0)
        {
            Imports.DropSendResult(state.SendFuture);
            state.SendFuture = 0;
        }
    }

    private static void DropReceive(WasiSocket state)
    {
        state.ReceiveEnded = true;
        if (state.ReceiveReadable != 0)
        {
            Imports.DropReceiveStream(state.ReceiveReadable);
            state.ReceiveReadable = 0;
        }

        if (state.ReceiveFuture != 0)
        {
            Imports.DropReceiveResult(state.ReceiveFuture);
            state.ReceiveFuture = 0;
        }
    }
}
