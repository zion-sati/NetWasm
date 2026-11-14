using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DnsContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void HostEntryDataMatchesDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "DnsFixture", Source, "DnsFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Net;
        namespace DnsFixture;
        public static class EntryPoint
        {
            // Lookups go through the host's name resolution (wasi:sockets), which the sockets suite's netwasm lanes
            // exercise; the host entry's data contract needs none.
            public static int Run(int input) => Portable(input) == 42 ? input + 1 : 3;
            public static int Portable(int input)
            {
                var entry = new IPHostEntry();
                if (entry.HostName != null || entry.Aliases != null || entry.AddressList != null) return 5;
                var aliases = new string[] { "alias" };
                var addresses = new IPAddress[] { IPAddress.Loopback, IPAddress.IPv6Loopback };
                entry.HostName = "example.com"; entry.Aliases = aliases; entry.AddressList = addresses;
                if (entry.HostName != "example.com" || !ReferenceEquals(entry.Aliases, aliases) || !ReferenceEquals(entry.AddressList, addresses)) return 6;
                aliases[0] = "changed";
                if (entry.Aliases[0] != "changed" || !entry.AddressList[0].Equals(IPAddress.Parse("127.0.0.1"))) return 7;
                entry.HostName = null; entry.Aliases = null; entry.AddressList = null;
                return entry.HostName == null && entry.Aliases == null && entry.AddressList == null ? input + 1 : 8;
            }
        }
        """;
}
