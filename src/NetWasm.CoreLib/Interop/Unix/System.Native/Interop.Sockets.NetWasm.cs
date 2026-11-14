// NetWasm adaptation: upstream's System.Native socket calls (Interop.Socket.cs and its siblings at
// dotnet/runtime 811225a4) as a managed shim over the program's platform network
// (PlatformServices.Network). A socket handle is the network's socket. The calls block until
// done; asynchronous work goes through SocketAsyncContext, which calls the network's async forms.
// Socket addresses are SocketAddressPal's byte layout. Errors come back as the errno SocketPal
// maps to SocketErrors. What WASI has no form of (poll, select, sendfile, multicast, packet
// information, Unix domain sockets) reports EOPNOTSUPP or ENOPROTOOPT.

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WebAssembly;
using Microsoft.Win32.SafeHandles;
using PlatformServices = System.Runtime.InteropServices.PlatformServices;

// NetWasm's Interop class is System.Interop.
namespace System
{
    internal static partial class Interop
    {
        [Flags]
        internal enum PollEvents : short
        {
            POLLNONE = 0x0000,
            POLLIN = 0x0001,
            POLLPRI = 0x0002,
            POLLOUT = 0x0004,
            POLLERR = 0x0008,
            POLLHUP = 0x0010,
            POLLNVAL = 0x0020,
        }

        internal struct PollEvent
        {
            internal int FileDescriptor;
            internal PollEvents Events;
            internal PollEvents TriggeredEvents;
        }

        internal static partial class Sys
        {
            internal unsafe struct MessageHeader
            {
                public byte* SocketAddress;
                public IOVector* IOVectors;
                public byte* ControlBuffer;
                public int SocketAddressLen;
                public int IOVectorCount;
                public int ControlBufferLen;
                public SocketFlags Flags;
            }

            internal enum MulticastOption : int
            {
                MULTICAST_ADD = 0,
                MULTICAST_DROP = 1,
                MULTICAST_IF = 2,
            }

            internal struct IPv4MulticastOption
            {
                public uint MulticastAddress;
                public uint LocalAddress;
                public int InterfaceIndex;
                private int _padding;
            }

            internal struct IPv6MulticastOption
            {
                public IPAddress Address;
                public int InterfaceIndex;
                private int _padding;
            }

            internal struct LingerOption
            {
                public int OnOff;
                public int Seconds;
            }

            internal const int IPv4AddressBytes = 4;
            internal const int IPv6AddressBytes = 16;
            internal const int MAX_IP_ADDRESS_BYTES = 16;

            internal unsafe struct IPAddress
            {
                public bool IsIPv6
                {
                    get { return _isIPv6 != 0; }
                    set { _isIPv6 = value ? 1u : 0u; }
                }

                internal fixed byte Address[MAX_IP_ADDRESS_BYTES];
                private uint _isIPv6;
                internal uint ScopeId;
            }

            internal struct IPPacketInformation
            {
                public IPAddress Address;
                public int InterfaceIndex;
                private int _padding;
            }

            [Flags]
            internal enum SocketEvents : int
            {
                None = 0x00,
                Read = 0x01,
                Write = 0x02,
                ReadClose = 0x04,
                Close = 0x08,
                Error = 0x10,
            }

            // What each socket was opened as, for GetSocketType, and the flags it was given.
            private sealed class SocketShape(AddressFamily family, SocketType type, ProtocolType protocol)
            {
                internal AddressFamily Family { get; } = family;
                internal SocketType Type { get; } = type;
                internal ProtocolType Protocol { get; } = protocol;
                internal bool NonBlocking { get; set; }
                internal bool Listening { get; set; }
            }

            private static readonly Dictionary<long, SocketShape> s_shapes = [];

            private static INetwork Network => PlatformServices.Network;

            private static long Socket(SafeHandle socket) => socket.DangerousGetHandle();

            private static Error Native(SocketError error) => error == SocketError.Success
                ? Error.SUCCESS
                : SocketErrorPal.GetNativeErrorForSocketError(error);

            internal static unsafe Error Socket(int addressFamily, int socketType, int protocolType, IntPtr* socket)
            {
                var error = Network.Open((AddressFamily)addressFamily, (SocketType)socketType, (ProtocolType)protocolType, out var opened);
                if (error == SocketError.Success)
                {
                    s_shapes[opened] = new SocketShape((AddressFamily)addressFamily, (SocketType)socketType, (ProtocolType)protocolType);
                    *socket = (IntPtr)opened;
                }

                return Native(error);
            }

            // Files own Close; SafeSocketHandle closes through this.
            internal static int CloseSocket(IntPtr fd)
            {
                s_shapes.Remove(fd);
                Network.Close(fd);
                return 0;
            }

            internal static Error Bind(SafeHandle socket, ProtocolType socketProtocolType, ReadOnlySpan<byte> socketAddress) =>
                Native(Network.Bind(Socket(socket), IPEndPointExtensions.CreateIPEndPoint(socketAddress)));

            internal static Error Listen(SafeHandle socket, int backlog)
            {
                var error = Network.Listen(Socket(socket), backlog);
                if (error == SocketError.Success && s_shapes.TryGetValue(Socket(socket), out var shape))
                {
                    shape.Listening = true;
                }

                return Native(error);
            }

            internal static unsafe Error Accept(SafeHandle socket, byte* socketAddress, int* socketAddressLen, IntPtr* acceptedFd)
            {
                ThrowIfNonBlocking(socket, "Accepting a connection");
                var error = Network.Accept(Socket(socket), out var accepted, out var remote);
                if (error == SocketError.Success)
                {
                    AdoptAccepted(socket.DangerousGetHandle(), (IntPtr)accepted);
                    *acceptedFd = (IntPtr)accepted;
                    Write(remote, socketAddress, socketAddressLen);
                }

                return Native(error);
            }

            // An accepted connection is a socket of its listener's shape.
            internal static void AdoptAccepted(IntPtr listener, IntPtr accepted)
            {
                if (s_shapes.TryGetValue(listener, out var shape))
                {
                    s_shapes[accepted] = new SocketShape(shape.Family, shape.Type, shape.Protocol);
                }
            }

            internal static unsafe Error Connect(SafeHandle socket, byte* socketAddress, int socketAddressLen)
            {
                ThrowIfNonBlocking(socket, "Connecting");
                return Native(Network.Connect(Socket(socket), EndPoint(socketAddress, socketAddressLen)));
            }

            internal static unsafe Error Connectx(SafeHandle socket, byte* socketAddress, int socketAddressLen, Span<byte> buffer,
                int bufferLen, int enableTfo, int* sent)
            {
                *sent = 0;
                return Error.EOPNOTSUPP;
            }

            internal static unsafe Error Send(SafeHandle socket, byte* buffer, int bufferLen, SocketFlags flags, int* sent)
            {
                var data = new ReadOnlySpan<byte>(buffer, bufferLen);
                var error = NonBlocking(socket)
                    ? Network.TrySend(Socket(socket), data, out var count)
                    : Network.Send(Socket(socket), data, out count);
                *sent = count;
                return Native(error);
            }

            internal static unsafe Error Receive(SafeHandle socket, byte* buffer, int bufferLen, SocketFlags flags, int* received)
            {
                var data = new Span<byte>(buffer, bufferLen);
                var error = NonBlocking(socket)
                    ? Network.TryReceive(Socket(socket), data, out var count)
                    : Network.Receive(Socket(socket), data, out count);
                *received = count;
                return Native(error);
            }

            // A message gathers its vectors into one send, to its address or the connected peer.
            internal static unsafe Error SendMessage(SafeHandle socket, MessageHeader* messageHeader, SocketFlags flags, long* sent)
            {
                var data = Gather(messageHeader);
                int count;
                SocketError error;
                if (IsStream(socket))
                {
                    error = NonBlocking(socket)
                        ? Network.TrySend(Socket(socket), data, out count)
                        : Network.Send(Socket(socket), data, out count);
                }
                else
                {
                    ThrowIfNonBlocking(socket, "Sending a datagram");
                    var remote = messageHeader->SocketAddress == null || messageHeader->SocketAddressLen == 0
                        ? null
                        : EndPoint(messageHeader->SocketAddress, messageHeader->SocketAddressLen);
                    error = Network.SendTo(Socket(socket), data, remote, out count);
                }

                *sent = count;
                return Native(error);
            }

            // A message scatters one receive over its vectors and names the sender.
            internal static unsafe Error ReceiveMessage(SafeHandle socket, MessageHeader* messageHeader, SocketFlags flags, long* received)
            {
                var total = 0;
                for (var index = 0; index < messageHeader->IOVectorCount; index++)
                {
                    total += checked((int)messageHeader->IOVectors[index].Count);
                }

                var data = new byte[total];
                int count;
                SocketError error;
                System.Net.IPEndPoint? remote = null;
                if (IsStream(socket))
                {
                    error = NonBlocking(socket)
                        ? Network.TryReceive(Socket(socket), data, out count)
                        : Network.Receive(Socket(socket), data, out count);
                }
                else
                {
                    ThrowIfNonBlocking(socket, "Receiving a datagram");
                    error = Network.ReceiveFrom(Socket(socket), data, out count, out remote);
                }

                Scatter(data.AsSpan(0, count), messageHeader);
                if (remote is not null && messageHeader->SocketAddress != null)
                {
                    Write(remote, messageHeader->SocketAddress, &messageHeader->SocketAddressLen);
                }

                messageHeader->ControlBufferLen = 0;
                messageHeader->Flags = SocketFlags.None;
                *received = count;
                return Native(error);
            }

            internal static Error Shutdown(SafeHandle socket, SocketShutdown how) => Native(Network.Shutdown(Socket(socket), how));

            internal static Error Shutdown(IntPtr socket, SocketShutdown how) => Native(Network.Shutdown(socket, how));

            internal static Error Disconnect(IntPtr socket) => Error.EOPNOTSUPP;

            internal static unsafe Error GetSockName(SafeHandle socket, byte* socketAddress, int* socketAddressLen)
            {
                var error = Network.GetLocalEndPoint(Socket(socket), out var endPoint);
                if (error == SocketError.Success)
                {
                    Write(endPoint, socketAddress, socketAddressLen);
                }

                return Native(error);
            }

            internal static unsafe Error GetPeerName(SafeHandle socket, byte* socketAddress, int* socketAddressLen)
            {
                var error = Network.GetRemoteEndPoint(Socket(socket), out var endPoint);
                if (error == SocketError.Success)
                {
                    Write(endPoint, socketAddress, socketAddressLen);
                }

                return Native(error);
            }

            internal static unsafe Error GetSockOpt(SafeHandle socket, SocketOptionLevel optionLevel, SocketOptionName optionName,
                byte* optionValue, int* optionLen) => GetSockOpt(socket.DangerousGetHandle(), optionLevel, optionName, optionValue, optionLen);

            internal static unsafe Error GetSockOpt(IntPtr socket, SocketOptionLevel optionLevel, SocketOptionName optionName,
                byte* optionValue, int* optionLen)
            {
                if (optionLevel == SocketOptionLevel.Socket && optionName == SocketOptionName.AcceptConnection)
                {
                    *(int*)optionValue = s_shapes.TryGetValue(socket, out var shape) && shape.Listening ? 1 : 0;
                    *optionLen = sizeof(int);
                    return Error.SUCCESS;
                }

                if (*optionLen < sizeof(int))
                {
                    return Error.EINVAL;
                }

                var error = Network.GetOption(socket, optionLevel, optionName, out var value);
                if (error == SocketError.Success)
                {
                    *(int*)optionValue = value;
                    *optionLen = sizeof(int);
                }

                return Native(error);
            }

            internal static unsafe Error GetRawSockOpt(SafeHandle socket, int optionLevel, int optionName, byte* optionValue, int* optionLen) =>
                Error.ENOPROTOOPT;

            internal static unsafe Error SetSockOpt(SafeHandle socket, SocketOptionLevel optionLevel, SocketOptionName optionName,
                byte* optionValue, int optionLen) => SetSockOpt(socket.DangerousGetHandle(), optionLevel, optionName, optionValue, optionLen);

            internal static unsafe Error SetSockOpt(IntPtr socket, SocketOptionLevel optionLevel, SocketOptionName optionName,
                byte* optionValue, int optionLen) =>
                optionLen < sizeof(int) ? Error.EINVAL : Native(Network.SetOption(socket, optionLevel, optionName, *(int*)optionValue));

            internal static unsafe Error SetRawSockOpt(SafeHandle socket, int optionLevel, int optionName, byte* optionValue, int optionLen) =>
                Error.ENOPROTOOPT;

            // The network reports each error with its call, so none is pending.
            internal static unsafe Error GetSocketErrorOption(SafeHandle socket, Error* socketError)
            {
                *socketError = Error.SUCCESS;
                return Error.SUCCESS;
            }

            internal static unsafe Error GetLingerOption(SafeHandle socket, LingerOption* option)
            {
                var error = Network.GetOption(Socket(socket), SocketOptionLevel.Socket, SocketOptionName.Linger, out var seconds);
                option->OnOff = seconds > 0 ? 1 : 0;
                option->Seconds = Math.Max(seconds, 0);
                return Native(error);
            }

            internal static unsafe Error SetLingerOption(SafeHandle socket, LingerOption* option) =>
                SetLingerOption(socket.DangerousGetHandle(), option);

            internal static unsafe Error SetLingerOption(IntPtr socket, LingerOption* option) =>
                Native(Network.SetOption(socket, SocketOptionLevel.Socket, SocketOptionName.Linger, option->OnOff != 0 ? option->Seconds : 0));

            // Socket rejects timeouts on WASI; elsewhere they are not enforced.
            internal static Error SetReceiveTimeout(SafeHandle socket, int millisecondsTimeout) => Error.SUCCESS;

            internal static Error SetSendTimeout(SafeHandle socket, int millisecondsTimeout) => Error.SUCCESS;

            internal static unsafe Error GetBytesAvailable(SafeHandle socket, int* available)
            {
                *available = 0;
                return Error.SUCCESS;
            }

            internal static unsafe Error GetAtOutOfBandMark(SafeHandle socket, int* atMark)
            {
                *atMark = 0;
                return Error.SUCCESS;
            }

            internal static Error GetSocketType(SafeSocketHandle socket, out AddressFamily addressFamily, out SocketType socketType,
                out ProtocolType protocolType, out bool isListening)
            {
                if (s_shapes.TryGetValue(socket.DangerousGetHandle(), out var shape))
                {
                    (addressFamily, socketType, protocolType, isListening) = (shape.Family, shape.Type, shape.Protocol, shape.Listening);
                    return Error.SUCCESS;
                }

                (addressFamily, socketType, protocolType, isListening) =
                    (AddressFamily.Unknown, SocketType.Unknown, ProtocolType.Unknown, false);
                return Error.EBADF;
            }

            internal static int GetMaximumAddressSize() => SocketAddressPal.MaxAddressSize;

            internal static unsafe Error Poll(PollEvent* pollEvents, uint eventCount, int timeout, uint* triggered)
            {
                *triggered = 0;
                return Error.EOPNOTSUPP;
            }

            internal static Error Poll(SafeHandle socket, PollEvents events, int timeout, out PollEvents triggered)
            {
                triggered = PollEvents.POLLNONE;
                return Error.EOPNOTSUPP;
            }

            internal static unsafe Error Select(Span<int> readFDs, int readFDsLength, Span<int> writeFDs, int writeFDsLength,
                Span<int> checkError, int checkErrorLength, int timeout, int maxFd, out int triggered)
            {
                triggered = 0;
                return Error.EOPNOTSUPP;
            }

            internal static unsafe Error GetIPv4MulticastOption(SafeHandle socket, MulticastOption multicastOption, IPv4MulticastOption* option) =>
                Error.ENOPROTOOPT;

            internal static unsafe Error SetIPv4MulticastOption(SafeHandle socket, MulticastOption multicastOption, IPv4MulticastOption* option) =>
                Error.ENOPROTOOPT;

            internal static unsafe Error GetIPv6MulticastOption(SafeHandle socket, MulticastOption multicastOption, IPv6MulticastOption* option) =>
                Error.ENOPROTOOPT;

            internal static unsafe Error SetIPv6MulticastOption(SafeHandle socket, MulticastOption multicastOption, IPv6MulticastOption* option) =>
                Error.ENOPROTOOPT;

            internal static int PlatformSupportsDualModeIPv4PacketInfo() => 0;

            internal static Error SendFile(SafeHandle out_fd, SafeHandle in_fd, long offset, long count, out long sent)
            {
                sent = 0;
                return Error.EOPNOTSUPP;
            }

            internal static int GetControlMessageBufferSize(int isIPv4, int isIPv6) => 0;

            internal static unsafe bool TryGetIPPacketInformation(MessageHeader* messageHeader, bool isIPv4, IPPacketInformation* packetInfo) => false;

            internal static unsafe void GetDomainSocketSizes(int* pathOffset, int* pathSize, int* addressSize)
            {
                // Linux's sockaddr_un: a 2-byte family and a 108-byte path.
                *pathOffset = 2;
                *pathSize = 108;
                *addressSize = 110;
            }

            // A socket is blocking unless made non-blocking; a file is always blocking.
            internal static partial class Fcntl
            {
                internal static int DangerousSetIsNonBlocking(IntPtr fd, int isNonBlocking)
                {
                    if (s_shapes.TryGetValue(fd, out var shape))
                    {
                        shape.NonBlocking = isNonBlocking != 0;
                    }

                    return 0;
                }

                internal static int SetIsNonBlocking(SafeHandle fd, int isNonBlocking) =>
                    DangerousSetIsNonBlocking(fd.DangerousGetHandle(), isNonBlocking);

                internal static int GetIsNonBlocking(SafeHandle fd, out bool isNonBlocking)
                {
                    isNonBlocking = fd is SafeSocketHandle && s_shapes.TryGetValue(fd.DangerousGetHandle(), out var shape) && shape.NonBlocking;
                    return 0;
                }

                // Every socket is the program's own, as if opened with FD_CLOEXEC.
                internal static int GetFD(SafeHandle fd) => 1;

                internal static int GetFD(IntPtr fd) => 1;
            }

            // A non-blocking socket (Socket.Blocking false) sends and receives only what the platform moves at once,
            // and otherwise reports EAGAIN, as on Linux. Connecting, accepting and datagrams would need readiness,
            // which the platform network does not report, so they throw on a non-blocking socket.
            private static bool NonBlocking(SafeHandle socket) => socket is SafeSocketHandle { IsNonBlocking: true };

            private static void ThrowIfNonBlocking(SafeHandle socket, string operation)
            {
                if (NonBlocking(socket))
                {
                    throw new PlatformNotSupportedException(
                        operation + " on a non-blocking socket needs readiness reporting, which the platform network does not have. Use the asynchronous socket methods instead.");
                }
            }

            private static bool IsStream(SafeHandle socket) =>
                !s_shapes.TryGetValue(Socket(socket), out var shape) || shape.Type == SocketType.Stream;

            private static unsafe System.Net.IPEndPoint EndPoint(byte* socketAddress, int socketAddressLen) =>
                IPEndPointExtensions.CreateIPEndPoint(new ReadOnlySpan<byte>(socketAddress, socketAddressLen));

            private static unsafe void Write(System.Net.IPEndPoint? endPoint, byte* socketAddress, int* socketAddressLen)
            {
                if (endPoint is null || socketAddress == null)
                {
                    *socketAddressLen = 0;
                    return;
                }

                var size = endPoint.AddressFamily == AddressFamily.InterNetworkV6
                    ? SocketAddressPal.IPv6AddressSize
                    : SocketAddressPal.IPv4AddressSize;
                var length = Math.Min(*socketAddressLen, size);
                Span<byte> buffer = stackalloc byte[SocketAddressPal.MaxAddressSize];
                endPoint.Serialize(buffer);
                buffer[..length].CopyTo(new Span<byte>(socketAddress, length));
                *socketAddressLen = size;
            }

            private static unsafe byte[] Gather(MessageHeader* messageHeader)
            {
                var total = 0;
                for (var index = 0; index < messageHeader->IOVectorCount; index++)
                {
                    total += checked((int)messageHeader->IOVectors[index].Count);
                }

                var data = new byte[total];
                var offset = 0;
                for (var index = 0; index < messageHeader->IOVectorCount; index++)
                {
                    var vector = messageHeader->IOVectors[index];
                    new ReadOnlySpan<byte>(vector.Base, checked((int)vector.Count)).CopyTo(data.AsSpan(offset));
                    offset += checked((int)vector.Count);
                }

                return data;
            }

            private static unsafe void Scatter(ReadOnlySpan<byte> data, MessageHeader* messageHeader)
            {
                var offset = 0;
                for (var index = 0; index < messageHeader->IOVectorCount && offset < data.Length; index++)
                {
                    var vector = messageHeader->IOVectors[index];
                    var take = Math.Min(checked((int)vector.Count), data.Length - offset);
                    data.Slice(offset, take).CopyTo(new Span<byte>(vector.Base, take));
                    offset += take;
                }
            }
        }
    }
}
