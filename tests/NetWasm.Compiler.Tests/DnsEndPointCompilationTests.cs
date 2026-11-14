using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DnsEndPointCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ManagedEndpointSemanticsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "EndpointFixture", Source, "EndpointFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Net;
        using System.Net.Sockets;
        namespace EndpointFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                // Socket addresses have Linux's layout.
                var v4 = new SocketAddress(AddressFamily.InterNetwork);
                if (v4.Family != AddressFamily.InterNetwork || v4.Size != 16) return 1;
                var v6 = new SocketAddress(AddressFamily.InterNetworkV6, 28);
                if (v6.Family != AddressFamily.InterNetworkV6 || v6.Size != 28) return 2;
                var caught = 0;
                try { _ = new SocketAddress(AddressFamily.InterNetwork, 1); return 3; } catch (ArgumentOutOfRangeException) { caught++; }
                return caught == 1 ? input + 1 : 4;
            }
            public static int Portable(int input)
            {
                var first = new DnsEndPoint("EXAMPLE.com", 443);
                var same = new DnsEndPoint("example.COM", 443, AddressFamily.Unspecified);
                EndPoint asBase = first;
                if (!first.Equals(same) || first.GetHashCode() != same.GetHashCode()) return 5;
                if (first.Host != "EXAMPLE.com" || first.Port != 443 || asBase.AddressFamily != AddressFamily.Unspecified) return 6;
                if (first.ToString() != "Unspecified/EXAMPLE.com:443") return 7;
                if (first.Equals(null) || first.Equals("EXAMPLE.com") || first.Equals(new DnsEndPoint("EXAMPLE.com", 80)) || first.Equals(new DnsEndPoint("EXAMPLE.com", 443, AddressFamily.InterNetwork))) return 8;
                if (new DnsEndPoint("host", 0).Port != 0 || new DnsEndPoint("host", 65535).Port != 65535) return 9;
                var errors = 0;
                try { _ = new DnsEndPoint(null, 0); return 10; } catch (ArgumentNullException ex) { if (ex.ParamName != "host") return 11; errors++; }
                try { _ = new DnsEndPoint("", 0); return 12; } catch (ArgumentException ex) { if (ex.ParamName != "host") return 13; errors++; }
                try { _ = new DnsEndPoint("host", -1); return 14; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "port") return 15; errors++; }
                try { _ = new DnsEndPoint("host", 65536); return 16; } catch (ArgumentOutOfRangeException) { errors++; }
                try { _ = new DnsEndPoint("host", 0, AddressFamily.Unix); return 17; } catch (ArgumentException ex) { if (ex.ParamName != "addressFamily") return 18; errors++; }
                EndPoint empty = new EmptyEndpoint();
                try { _ = empty.AddressFamily; return 19; } catch (NotImplementedException) { errors++; }
                try { _ = empty.Create(null); return 20; } catch (NotImplementedException) { errors++; }
                try { _ = empty.Serialize(); return 21; } catch (NotImplementedException) { errors++; }
                return errors == 8 ? input + 1 : 22;
            }
        }
        public sealed class EmptyEndpoint : EndPoint { }
        """;
}
