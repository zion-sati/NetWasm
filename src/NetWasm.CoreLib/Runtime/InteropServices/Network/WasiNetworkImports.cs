namespace System.Runtime.InteropServices.Network;

using System.Runtime.InteropServices.WebAssembly;

// wasi:sockets 0.3: TCP and UDP sockets and name lookup. A synchronous method passes up to 16
// flat parameters (an ip-socket-address flattens to a tag and 11 values) and stores a result of
// more than one value at result. An async method is lowered async: its parameters pass through
// a record in memory when there are more than four, its result is stored at result, and the
// call returns its subtask's status. A TCP connection's data moves through the stream<u8> a
// program hands send and the one receive returns; a listener's connections arrive on the
// stream<tcp-socket> listen returns.
internal static class WasiNetworkImports
{
    private const string Types = "wasi:sockets/types@0.3.2";
    private const string Lookup = "wasi:sockets/ip-name-lookup@0.3.2";

    [WitImport(Types, "[static]tcp-socket.create")]
    internal static extern void CreateTcp(int family, nuint result);

    [WitImport(Types, "[static]udp-socket.create")]
    internal static extern void CreateUdp(int family, nuint result);

    [WitImport(Types, "[resource-drop]tcp-socket")]
    internal static extern void DropTcp(int socket);

    [WitImport(Types, "[resource-drop]udp-socket")]
    internal static extern void DropUdp(int socket);

    [WitImport(Types, "[method]tcp-socket.bind")]
    internal static extern void BindTcp(int socket, int tag, int a0, int a1, int a2, int a3, int a4, int a5, int a6,
        int a7, int a8, int a9, int a10, nuint result);

    [WitImport(Types, "[method]udp-socket.bind")]
    internal static extern void BindUdp(int socket, int tag, int a0, int a1, int a2, int a3, int a4, int a5, int a6,
        int a7, int a8, int a9, int a10, nuint result);

    [WitImport(Types, "[method]udp-socket.connect")]
    internal static extern void ConnectUdp(int socket, int tag, int a0, int a1, int a2, int a3, int a4, int a5, int a6,
        int a7, int a8, int a9, int a10, nuint result);

    [WitImport(Types, "[method]udp-socket.disconnect")]
    internal static extern void DisconnectUdp(int socket, nuint result);

    // socket, remote-address.
    [ComponentBuiltIn(Types, "[async-lower][method]tcp-socket.connect")]
    internal static extern int ConnectTcp(nuint parameters, nuint result);

    [WitImport(Types, "[method]tcp-socket.set-listen-backlog-size")]
    internal static extern void SetListenBacklog(int socket, long value, nuint result);

    // Stores result<stream<tcp-socket>, error-code> at result.
    [WitImport(Types, "[method]tcp-socket.listen")]
    internal static extern void Listen(int socket, nuint result);

    [ComponentBuiltIn(Types, "[async-lower][stream-read-0][method]tcp-socket.listen")]
    internal static extern int ReadConnections(int readable, nuint buffer, nuint count);

    [ComponentBuiltIn(Types, "[stream-cancel-read-0][method]tcp-socket.listen")]
    internal static extern int CancelReadConnections(int readable);

    [ComponentBuiltIn(Types, "[stream-drop-readable-0][method]tcp-socket.listen")]
    internal static extern void DropConnections(int readable);

    // The readable end in the low 32 bits, the writable end in the high 32.
    [ComponentBuiltIn(Types, "[stream-new-0][method]tcp-socket.send")]
    internal static extern long CreateSendStream();

    // Hands the host the readable end; returns the future<result<_, error-code>>.
    [WitImport(Types, "[method]tcp-socket.send")]
    internal static extern int Send(int socket, int readable);

    [ComponentBuiltIn(Types, "[async-lower][stream-write-0][method]tcp-socket.send")]
    internal static extern int WriteBytes(int writable, nuint buffer, nuint count);

    [ComponentBuiltIn(Types, "[stream-cancel-write-0][method]tcp-socket.send")]
    internal static extern int CancelWriteBytes(int writable);

    [ComponentBuiltIn(Types, "[stream-drop-writable-0][method]tcp-socket.send")]
    internal static extern void DropSendStream(int writable);

    [ComponentBuiltIn(Types, "[async-lower][future-read-1][method]tcp-socket.send")]
    internal static extern int ReadSendResult(int readable, nuint result);

    [ComponentBuiltIn(Types, "[future-drop-readable-1][method]tcp-socket.send")]
    internal static extern void DropSendResult(int readable);

    // Stores the readable end of the stream<u8> and its future<result<_, error-code>> at result.
    [WitImport(Types, "[method]tcp-socket.receive")]
    internal static extern void Receive(int socket, nuint result);

    [ComponentBuiltIn(Types, "[async-lower][stream-read-0][method]tcp-socket.receive")]
    internal static extern int ReadBytes(int readable, nuint buffer, nuint count);

    [ComponentBuiltIn(Types, "[stream-cancel-read-0][method]tcp-socket.receive")]
    internal static extern int CancelReadBytes(int readable);

    [ComponentBuiltIn(Types, "[stream-drop-readable-0][method]tcp-socket.receive")]
    internal static extern void DropReceiveStream(int readable);

    [ComponentBuiltIn(Types, "[async-lower][future-read-1][method]tcp-socket.receive")]
    internal static extern int ReadReceiveResult(int readable, nuint result);

    [ComponentBuiltIn(Types, "[future-drop-readable-1][method]tcp-socket.receive")]
    internal static extern void DropReceiveResult(int readable);

    [WitImport(Types, "[method]tcp-socket.get-local-address")]
    internal static extern void TcpLocalAddress(int socket, nuint result);

    [WitImport(Types, "[method]tcp-socket.get-remote-address")]
    internal static extern void TcpRemoteAddress(int socket, nuint result);

    [WitImport(Types, "[method]udp-socket.get-local-address")]
    internal static extern void UdpLocalAddress(int socket, nuint result);

    [WitImport(Types, "[method]udp-socket.get-remote-address")]
    internal static extern void UdpRemoteAddress(int socket, nuint result);

    // socket, data, remote-address.
    [ComponentBuiltIn(Types, "[async-lower][method]udp-socket.send")]
    internal static extern int SendDatagram(nuint parameters, nuint result);

    [ComponentBuiltIn(Types, "[async-lower][method]udp-socket.receive")]
    internal static extern int ReceiveDatagram(int socket, nuint result);

    [WitImport(Types, "[method]tcp-socket.get-keep-alive-enabled")]
    internal static extern void GetKeepAlive(int socket, nuint result);

    [WitImport(Types, "[method]tcp-socket.set-keep-alive-enabled")]
    internal static extern void SetKeepAlive(int socket, int value, nuint result);

    [WitImport(Types, "[method]tcp-socket.get-hop-limit")]
    internal static extern void GetTcpHopLimit(int socket, nuint result);

    [WitImport(Types, "[method]tcp-socket.set-hop-limit")]
    internal static extern void SetTcpHopLimit(int socket, int value, nuint result);

    [WitImport(Types, "[method]udp-socket.get-unicast-hop-limit")]
    internal static extern void GetUdpHopLimit(int socket, nuint result);

    [WitImport(Types, "[method]udp-socket.set-unicast-hop-limit")]
    internal static extern void SetUdpHopLimit(int socket, int value, nuint result);

    [WitImport(Types, "[method]tcp-socket.get-receive-buffer-size")]
    internal static extern void GetTcpReceiveBuffer(int socket, nuint result);

    [WitImport(Types, "[method]tcp-socket.set-receive-buffer-size")]
    internal static extern void SetTcpReceiveBuffer(int socket, long value, nuint result);

    [WitImport(Types, "[method]tcp-socket.get-send-buffer-size")]
    internal static extern void GetTcpSendBuffer(int socket, nuint result);

    [WitImport(Types, "[method]tcp-socket.set-send-buffer-size")]
    internal static extern void SetTcpSendBuffer(int socket, long value, nuint result);

    [WitImport(Types, "[method]udp-socket.get-receive-buffer-size")]
    internal static extern void GetUdpReceiveBuffer(int socket, nuint result);

    [WitImport(Types, "[method]udp-socket.set-receive-buffer-size")]
    internal static extern void SetUdpReceiveBuffer(int socket, long value, nuint result);

    [WitImport(Types, "[method]udp-socket.get-send-buffer-size")]
    internal static extern void GetUdpSendBuffer(int socket, nuint result);

    [WitImport(Types, "[method]udp-socket.set-send-buffer-size")]
    internal static extern void SetUdpSendBuffer(int socket, long value, nuint result);

    // Stores result<list<ip-address>, error-code> at result.
    [ComponentBuiltIn(Lookup, "[async-lower]resolve-addresses")]
    internal static extern int ResolveAddresses(nuint name, nuint nameLength, nuint result);
}
