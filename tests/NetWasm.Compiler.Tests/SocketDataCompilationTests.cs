using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SocketDataCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OptionAndResultSemanticsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SocketDataFixture", Source, "SocketDataFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Net;
        using System.Net.Sockets;
        namespace SocketDataFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var linger = new LingerOption(true, -1);
                if (!linger.Enabled || linger.LingerTime != -1) return 1;
                linger.Enabled = false; linger.LingerTime = 65536;
                if (linger.Enabled || linger.LingerTime != 65536) return 2;
                var group = IPAddress.Parse("239.1.2.3");
                var option = new MulticastOption(group);
                if (!ReferenceEquals(option.Group, group) || !option.LocalAddress.Equals(IPAddress.Any) || option.InterfaceIndex != 0) return 3;
                option.InterfaceIndex = 0x00ffffff;
                if (option.LocalAddress != null || option.InterfaceIndex != 0x00ffffff) return 4;
                option.LocalAddress = IPAddress.Loopback;
                if (option.InterfaceIndex != 0 || !option.LocalAddress.Equals(IPAddress.Loopback)) return 5;
                option.LocalAddress = null;
                if (option.LocalAddress != null) return 6;
                var explicitIndex = new MulticastOption(group, 10);
                var explicitAddress = new MulticastOption(group, IPAddress.Loopback);
                if (explicitIndex.InterfaceIndex != 10 || explicitIndex.LocalAddress != null || explicitAddress.InterfaceIndex != 0) return 7;
                var ipv6 = new IPv6MulticastOption(IPAddress.Parse("ff02::1"), uint.MaxValue);
                if (ipv6.InterfaceIndex != uint.MaxValue) return 8;
                ipv6.InterfaceIndex = 0;
                if (new IPv6MulticastOption(ipv6.Group).InterfaceIndex != 0) return 9;
                var errors = 0;
                try { _ = new MulticastOption(null); return 10; } catch (ArgumentNullException) { errors++; }
                try { _ = new MulticastOption(group, (IPAddress)null); return 11; } catch (ArgumentNullException) { errors++; }
                try { _ = new MulticastOption(group, -1); return 12; } catch (ArgumentOutOfRangeException) { errors++; }
                try { option.InterfaceIndex = 0x01000000; return 13; } catch (ArgumentOutOfRangeException) { errors++; }
                try { option.Group = null; return 14; } catch (ArgumentNullException) { errors++; }
                try { _ = new IPv6MulticastOption(null); return 15; } catch (ArgumentNullException) { errors++; }
                try { ipv6.InterfaceIndex = -1; return 16; } catch (ArgumentOutOfRangeException) { errors++; }
                try { ipv6.InterfaceIndex = 0x100000000L; return 17; } catch (ArgumentOutOfRangeException) { errors++; }
                try { ipv6.Group = null; return 18; } catch (ArgumentNullException) { errors++; }
                var bytes = new byte[] { 42 };
                var endpoint = new IPEndPoint(IPAddress.Loopback, 1234);
                var received = new UdpReceiveResult(bytes, endpoint);
                var same = new UdpReceiveResult(bytes, new IPEndPoint(IPAddress.Loopback, 1234));
                if (!received.Equals(same) || received != same || received.GetHashCode() != same.GetHashCode()) return 19;
                if (!ReferenceEquals(received.Buffer, bytes) || !ReferenceEquals(received.RemoteEndPoint, endpoint)) return 20;
                if (received == new UdpReceiveResult(new byte[] { 42 }, endpoint)) return 21;
                UdpReceiveResult empty = default;
                if (empty.Buffer != null || empty.RemoteEndPoint != null || empty.GetHashCode() != 0 || !empty.Equals(default(UdpReceiveResult))) return 22;
                try { _ = new UdpReceiveResult(null, endpoint); return 23; } catch (ArgumentNullException) { errors++; }
                try { _ = new UdpReceiveResult(bytes, null); return 24; } catch (ArgumentNullException) { errors++; }
                var information = new SocketInformation { ProtocolInformation = bytes, Options = SocketInformationOptions.Connected | SocketInformationOptions.NonBlocking };
                if (!ReferenceEquals(information.ProtocolInformation, bytes) || !information.Options.HasFlag(SocketInformationOptions.Connected)) return 25;
                IPPacketInformation packet = default;
                if (packet.Address != null || packet.Interface != 0 || packet.GetHashCode() != 0 || packet != default(IPPacketInformation)) return 26;
                var from = new SocketReceiveFromResult { ReceivedBytes = 1, RemoteEndPoint = endpoint };
                var message = new SocketReceiveMessageFromResult { ReceivedBytes = 1, RemoteEndPoint = endpoint, SocketFlags = SocketFlags.Peek, PacketInformation = packet };
                if (from.ReceivedBytes != 1 || !ReferenceEquals(from.RemoteEndPoint, endpoint) || message.SocketFlags != SocketFlags.Peek || message.PacketInformation != packet) return 27;
                return errors == 11 ? input + 1 : 28;
            }
        }
        """;
}
