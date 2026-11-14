// From the compiler harness's NetworkInformationCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
namespace NetworkInformationFixture;

sealed class Addresses : IPAddressCollection { }

sealed class Gateway : GatewayIPAddressInformation
{
    public override IPAddress Address => IPAddress.Loopback;
}

sealed class Connection : TcpConnectionInformation
{
    public override IPEndPoint LocalEndPoint => new IPEndPoint(IPAddress.Loopback, 80);
    public override IPEndPoint RemoteEndPoint => new IPEndPoint(IPAddress.Loopback, 8080);
    public override TcpState State => TcpState.Established;
}

sealed class Interface : NetworkInterface { }

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // Physical addresses parse, format, compare and copy their bytes as upstream does.
        var mac = PhysicalAddress.Parse("00-11-22-AA-bb-0F");
        if (mac.ToString() != "001122AABB0F" || mac.GetAddressBytes().Length != 6 || mac.GetAddressBytes()[3] != 0xAA) return 1;
        if (!mac.Equals(PhysicalAddress.Parse("001122AABB0F")) || mac.GetHashCode() != PhysicalAddress.Parse("00:11:22:AA:BB:0F").GetHashCode()) return 2;
        if (PhysicalAddress.None.GetAddressBytes().Length != 0 || PhysicalAddress.TryParse("not-a-mac", out _) || !Throws<FormatException>(() => PhysicalAddress.Parse("zz"))) return 3;
        var owned = new PhysicalAddress(new byte[] { 1, 2, 3 });
        owned.GetAddressBytes()[0] = 9;
        if (owned.GetAddressBytes()[0] != 1) return 4;

        // Enum constants keep the reference values.
        if ((int)NetworkInterfaceType.Ethernet != 6 || (int)NetworkInterfaceType.Wwanpp2 != 244 || (int)OperationalStatus.Up != 1 || (int)TcpState.Established != 5) return 5;
        if ((int)IPStatus.TimedOut != 11010 || (int)IPStatus.Unknown != -1 || (int)ScopeLevel.Global != 14 || (int)SuffixOrigin.Random != 5) return 6;
        if ((int)ProtocolFamily.InterNetworkV6 != 23 || (int)ProtocolFamily.Unknown != -1 || (int)ProtocolFamily.Max != 29 || (int)DuplicateAddressDetectionState.Preferred != 4) return 7;

        // Options and payload types carry their values.
        var options = new PingOptions(64, true);
        if (options.Ttl != 64 || !options.DontFragment || new PingOptions().Ttl != 128 || new PingOptions().DontFragment) return 8;
        if (!Throws<ArgumentOutOfRangeException>(() => new PingOptions(0, false))) return 9;
        var inner = new InvalidOperationException("inner");
        if (new PingException("ping", inner).InnerException != inner) return 10;
        if (new NetworkInformationException(5).ErrorCode != 5) return 19;

        // Collections are read-only and abstract contracts dispatch to the caller's overrides.
        var addresses = new Addresses();
        if (!addresses.IsReadOnly || !Throws<NotSupportedException>(() => addresses.Add(IPAddress.Loopback)) || !Throws<NotImplementedException>(() => _ = addresses.Count)) return 11;
        GatewayIPAddressInformation gateway = new Gateway();
        TcpConnectionInformation connection = new Connection();
        if (!gateway.Address.Equals(IPAddress.Loopback) || connection.RemoteEndPoint.Port != 8080 || connection.State != TcpState.Established) return 12;

        // Members a provider does not override keep upstream's not-implemented defaults.
        NetworkInterface nic = new Interface();
        if (!Throws<NotImplementedException>(() => _ = nic.Name) || !Throws<NotImplementedException>(() => nic.GetPhysicalAddress()) || !Throws<NotImplementedException>(() => nic.Supports(NetworkInterfaceComponent.IPv4))) return 13;

        using var ping = new Ping();
        ping.SendAsyncCancel();
#if !DESKTOP_ORACLE
        // The host has no interface discovery, change notifications or ICMP transport.
        if (!Throws<PlatformNotSupportedException>(() => NetworkInterface.GetAllNetworkInterfaces()) || !Throws<PlatformNotSupportedException>(() => NetworkInterface.GetIsNetworkAvailable())) return 14;
        if (!Throws<PlatformNotSupportedException>(() => IPGlobalProperties.GetIPGlobalProperties()) || !Throws<PlatformNotSupportedException>(() => _ = NetworkInterface.LoopbackInterfaceIndex)) return 15;
        if (NetworkChange.IsSupported || !Throws<PlatformNotSupportedException>(() => NetworkChange.NetworkAddressChanged += (_, _) => { })) return 16;
        if (!Throws<PlatformNotSupportedException>(() => ping.Send(IPAddress.Loopback)) || !Throws<PlatformNotSupportedException>(() => ping.Send("127.0.0.1"))) return 17;
        // The parameterless constructor reports the last P/Invoke error, with the C library's message for it.
        if (new NetworkInformationException().ErrorCode != System.Runtime.InteropServices.Marshal.GetLastPInvokeError()) return 18;
#endif
        return input + 1;
    }
}
