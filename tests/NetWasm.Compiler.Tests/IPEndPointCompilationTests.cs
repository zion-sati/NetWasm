using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class IPEndPointCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AddressPortSemanticsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "IPEndPointFixture", Source, "IPEndPointFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Net;
        using System.Net.Sockets;
        namespace IPEndPointFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                // Serialization is the socket address layout, as on Linux.
                EndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 80);
                SocketAddress serialized = endpoint.Serialize();
                if (serialized.Family != AddressFamily.InterNetwork || serialized.Size != 16) return 1;
                if (!endpoint.Equals(endpoint.Create(serialized))) return 2;
                SocketAddress v6 = new IPEndPoint(IPAddress.IPv6Loopback, 443).Serialize();
                if (v6.Family != AddressFamily.InterNetworkV6 || v6.Size != 28) return 3;
                var caught = 0;
                try { endpoint.Create(null!); return 4; } catch (ArgumentNullException) { caught++; }
                return caught == 1 ? input + 1 : 5;
            }
            public static int Portable(int input)
            {
                var endpoint = new IPEndPoint(IPAddress.Loopback, 80);
                var same = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 80);
                if (!endpoint.Equals(same) || endpoint.GetHashCode() != same.GetHashCode() || endpoint.ToString() != "127.0.0.1:80") return 6;
                if (endpoint.Equals(null) || endpoint.Equals(new IPEndPoint(IPAddress.Loopback, 81))) return 7;
                endpoint.Address = IPAddress.IPv6Loopback; endpoint.Port = IPEndPoint.MaxPort;
                EndPoint asBase = endpoint;
                if (asBase.AddressFamily != AddressFamily.InterNetworkV6 || endpoint.ToString() != "[::1]:65535") return 8;
                if (new IPEndPoint(0, IPEndPoint.MinPort).ToString() != "0.0.0.0:0") return 9;
                var caught = 0;
                try { _ = new IPEndPoint((IPAddress)null, 80); return 10; } catch (ArgumentNullException ex) { if (ex.ParamName != "address") return 11; caught++; }
                try { _ = new IPEndPoint(IPAddress.Loopback, -1); return 12; } catch (ArgumentOutOfRangeException) { caught++; }
                try { _ = new IPEndPoint(0, 65536); return 13; } catch (ArgumentOutOfRangeException) { caught++; }
                try { endpoint.Port = -1; return 14; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "value") return 15; caught++; }
                try { endpoint.Address = null; return 16; } catch (ArgumentNullException) { caught++; }
                if (!IPEndPoint.TryParse("[::1]:443", out var parsed) || parsed.ToString() != "[::1]:443") return 17;
                if (IPEndPoint.TryParse("127.0.0.1:65536", out _) || IPEndPoint.TryParse("invalid", out _)) return 18;
                return caught == 5 ? input + 1 : 19;
            }
        }
        """;
}
