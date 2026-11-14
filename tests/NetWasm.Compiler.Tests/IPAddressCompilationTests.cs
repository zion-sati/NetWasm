using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class IPAddressCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedAddressSemanticsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "IPAddressFixture", Source, "IPAddressFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        #pragma warning disable CS0618
        using System;
        using System.Net;
        using System.Net.Sockets;
        namespace IPAddressFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                try { _ = IPAddress.Parse("fe80::1%netwasm-interface"); return 1; }
                catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("interface")) return 2; }
                return input + 1;
            }
            public static int Portable(int input)
            {
                var inputs = new string[] { "127.0.0.1", "127.1", "0x7f000001", "0177.0.0.1", "192.0.2.1", "::1", "::", "2001:db8:0:0:0:0:0:1", "::ffff:192.0.2.1", "fe80::1%12" };
                var expected = new string[] { "127.0.0.1", "127.0.0.1", "127.0.0.1", "127.0.0.1", "192.0.2.1", "::1", "::", "2001:db8::1", "::ffff:192.0.2.1", "fe80::1%12" };
                for (var i = 0; i < inputs.Length; i++)
                {
                    var address = IPAddress.Parse(inputs[i]);
                    if (address.ToString() != expected[i]) return 3;
                    if (!IPAddress.TryParse(inputs[i].AsSpan(), out var same) || !address.Equals(same) || address.GetHashCode() != same.GetHashCode()) return 4;
                    var bytes = address.GetAddressBytes();
                    var copy = bytes.Length == 16 ? new IPAddress(bytes, address.ScopeId) : new IPAddress(bytes);
                    if (!copy.Equals(address)) return 5;
                    var buffer = new byte[16];
                    if (!address.TryWriteBytes(buffer, out var written) || written != bytes.Length) return 6;
                    for (var j = 0; j < written; j++) if (buffer[j] != bytes[j]) return 7;
                    if (address.TryWriteBytes(new byte[1], out var tooShort) || tooShort != 0) return 8;
                    var chars = new char[64];
                    if (!address.TryFormat(chars, out var formatted) || new string(chars, 0, formatted) != expected[i]) return 9;
                    if (address.TryFormat(new char[1], out var shortFormat) || shortFormat != 0) return 10;
                }
                if (IPAddress.TryParse("not-an-address", out _) || IPAddress.TryParse((string)null, out _)) return 11;
                try { IPAddress.Parse("not-an-address"); return 12; }
                catch (FormatException ex) { if (ex.InnerException is not SocketException socket || socket.SocketErrorCode != SocketError.InvalidArgument) return 13; }
                try { _ = new IPAddress(new byte[3]); return 14; } catch (ArgumentException ex) { if (ex.ParamName != "address") return 15; }
                try { _ = new IPAddress(-1L); return 16; } catch (ArgumentOutOfRangeException) { }
                var ipv4 = IPAddress.Parse("192.0.2.1");
                var mapped = ipv4.MapToIPv6();
                if (!mapped.IsIPv4MappedToIPv6 || !mapped.MapToIPv4().Equals(ipv4)) return 17;
                if (!IPAddress.IsLoopback(IPAddress.Loopback) || !IPAddress.IsLoopback(IPAddress.IPv6Loopback) || IPAddress.IsLoopback(ipv4)) return 18;
                if (!IPAddress.Parse("ff02::1").IsIPv6Multicast || !IPAddress.Parse("fe80::1").IsIPv6LinkLocal || !IPAddress.Parse("fec0::1").IsIPv6SiteLocal) return 19;
                var scoped = IPAddress.Parse("fe80::1%12");
                _ = scoped.GetHashCode(); _ = scoped.ToString(); scoped.ScopeId = 13;
                if (scoped.ScopeId != 13 || scoped.ToString() != "fe80::1%13" || scoped.Equals(IPAddress.Parse("fe80::1%12"))) return 20;
                try { _ = ipv4.ScopeId; return 21; } catch (SocketException ex) { if (ex.SocketErrorCode != SocketError.OperationNotSupported) return 22; }
                try { IPAddress.IPv6Any.ScopeId = 12; return 23; } catch (SocketException) { }
                if (IPAddress.NetworkToHostOrder(IPAddress.HostToNetworkOrder((short)1234)) != 1234 || IPAddress.NetworkToHostOrder(IPAddress.HostToNetworkOrder(12345678)) != 12345678 || IPAddress.NetworkToHostOrder(IPAddress.HostToNetworkOrder(1234567890123L)) != 1234567890123L) return 24;
                return input + 1;
            }
        }
        """;
}
