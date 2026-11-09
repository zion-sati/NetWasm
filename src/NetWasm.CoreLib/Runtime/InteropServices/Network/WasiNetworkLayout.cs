namespace System.Runtime.InteropServices.Network;

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.FileSystem;
using System.Runtime.InteropServices.WebAssembly;

// wasi:sockets 0.3's canonical ABI layouts, sized for the pointer width P:
// - ip-socket-address: tag u8 at 0, payload at 4; ipv4 {port u16, address u8x4 at 2},
//   ipv6 {port u16, flow-info u32 at 4, address u16x8 at 8, scope-id u32 at 24}; 32 bytes.
// - error-code: tag u8 at 0, other's option<string> at P; 4P bytes.
// - result<T, error-code>: tag u8 at 0, payload at max(align T, P).
// - ip-address: tag u8 at 0, payload at 2 (u8x4 or u16x8); 18 bytes, align 2.
internal static class WasiNetworkLayout
{
    internal const int Blocked = -1;
    internal const uint Completed = 0;
    internal const uint Dropped = 1;
    internal const int FamilyIPv4 = 0;
    internal const int FamilyIPv6 = 1;
    internal const nuint SocketAddressSize = 32;
    internal const nuint IPAddressSize = 18;

    internal static readonly nuint Pointer = unchecked((nuint)nuint.Size);

    // result<_, error-code> and result<handle, error-code>.
    internal static nuint SmallResultSize => Pointer * 5;

    // result<ip-socket-address, error-code>: the address at max(4, P).
    internal static nuint AddressResultSize => Pointer + SocketAddressSize;

    internal static int Family(AddressFamily family) =>
        family == AddressFamily.InterNetworkV6 ? FamilyIPv6 : FamilyIPv4;

    // An ip-socket-address flattened for a synchronous call: its tag and 11 values.
    internal static int[] Flatten(IPEndPoint endPoint)
    {
        var flat = new int[12];
        var bytes = endPoint.Address.GetAddressBytes();
        flat[1] = endPoint.Port;
        if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            flat[0] = FamilyIPv6;
            flat[2] = 0;
            for (var index = 0; index < 8; index++)
            {
                flat[3 + index] = (bytes[index * 2] << 8) | bytes[index * 2 + 1];
            }

            flat[11] = unchecked((int)endPoint.Address.ScopeId);
        }
        else
        {
            for (var index = 0; index < 4; index++)
            {
                flat[2 + index] = bytes[index];
            }
        }

        return flat;
    }

    internal static void WriteAddress(nuint address, IPEndPoint endPoint)
    {
        var bytes = endPoint.Address.GetAddressBytes();
        var payload = address + 4;
        CanonicalAbi.WriteUInt16(payload, 0, unchecked((ushort)endPoint.Port));
        if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            CanonicalAbi.WriteByte(address, 0, FamilyIPv6);
            CanonicalAbi.WriteInt32(payload, 4, 0);
            for (var index = 0; index < 8; index++)
            {
                CanonicalAbi.WriteUInt16(payload, 8 + unchecked((nuint)index) * 2,
                    unchecked((ushort)((bytes[index * 2] << 8) | bytes[index * 2 + 1])));
            }

            CanonicalAbi.WriteInt32(payload, 24, unchecked((int)endPoint.Address.ScopeId));
        }
        else
        {
            CanonicalAbi.WriteByte(address, 0, FamilyIPv4);
            for (var index = 0; index < 4; index++)
            {
                CanonicalAbi.WriteByte(payload, 2 + unchecked((nuint)index), bytes[index]);
            }
        }
    }

    internal static IPEndPoint ReadAddress(nuint address)
    {
        var payload = address + 4;
        var port = CanonicalAbi.ReadUInt16(payload, 0);
        if (CanonicalAbi.ReadByte(address, 0) == FamilyIPv6)
        {
            var bytes = new byte[16];
            for (var index = 0; index < 8; index++)
            {
                var segment = CanonicalAbi.ReadUInt16(payload, 8 + unchecked((nuint)index) * 2);
                bytes[index * 2] = unchecked((byte)(segment >> 8));
                bytes[index * 2 + 1] = unchecked((byte)segment);
            }

            return new IPEndPoint(new IPAddress(bytes, unchecked((uint)CanonicalAbi.ReadInt32(payload, 24))), port);
        }

        return new IPEndPoint(new IPAddress(new[]
        {
            CanonicalAbi.ReadByte(payload, 2), CanonicalAbi.ReadByte(payload, 3),
            CanonicalAbi.ReadByte(payload, 4), CanonicalAbi.ReadByte(payload, 5),
        }), port);
    }

    // An ip-address list element.
    internal static IPAddress ReadIPAddress(nuint address)
    {
        if (CanonicalAbi.ReadByte(address, 0) == FamilyIPv6)
        {
            var bytes = new byte[16];
            for (var index = 0; index < 8; index++)
            {
                var segment = CanonicalAbi.ReadUInt16(address, 2 + unchecked((nuint)index) * 2);
                bytes[index * 2] = unchecked((byte)(segment >> 8));
                bytes[index * 2 + 1] = unchecked((byte)segment);
            }

            return new IPAddress(bytes);
        }

        return new IPAddress(new[]
        {
            CanonicalAbi.ReadByte(address, 2), CanonicalAbi.ReadByte(address, 3),
            CanonicalAbi.ReadByte(address, 4), CanonicalAbi.ReadByte(address, 5),
        });
    }

    // result<_, error-code>, or a result whose payload starts at P, such as a handle.
    internal static SocketError ReadResult(nuint result) =>
        CanonicalAbi.ReadByte(result, 0) == 0 ? SocketError.Success : ReadError(result + Pointer);

    // ip-name-lookup's error-code at the address; other's message is released.
    internal static SocketError ReadLookupError(nuint error)
    {
        var tag = CanonicalAbi.ReadByte(error, 0);
        if (tag == 5 && CanonicalAbi.ReadByte(error + Pointer, 0) == 1)
        {
            WasiFileSystemLayout.TakeString(error + Pointer + Pointer);
        }

        return tag switch
        {
            0 => SocketError.AccessDenied,
            1 => SocketError.InvalidArgument,
            2 => SocketError.HostNotFound,
            3 => SocketError.TryAgain,
            4 => SocketError.NoRecovery,
            _ => SocketError.SocketError,
        };
    }

    // The error-code at the address; other's message is released.
    internal static SocketError ReadError(nuint error)
    {
        var tag = CanonicalAbi.ReadByte(error, 0);
        if (tag == 14 && CanonicalAbi.ReadByte(error + Pointer, 0) == 1)
        {
            WasiFileSystemLayout.TakeString(error + Pointer + Pointer);
        }

        return tag switch
        {
            0 => SocketError.AccessDenied,
            1 => SocketError.OperationNotSupported,
            2 => SocketError.InvalidArgument,
            3 => SocketError.NoBufferSpaceAvailable,
            4 => SocketError.TimedOut,
            5 => SocketError.InvalidArgument,
            6 => SocketError.AddressNotAvailable,
            7 => SocketError.AddressAlreadyInUse,
            8 => SocketError.HostUnreachable,
            9 => SocketError.ConnectionRefused,
            10 => SocketError.ConnectionAborted,
            11 => SocketError.ConnectionReset,
            12 => SocketError.ConnectionAborted,
            13 => SocketError.MessageSize,
            _ => SocketError.SocketError,
        };
    }
}
