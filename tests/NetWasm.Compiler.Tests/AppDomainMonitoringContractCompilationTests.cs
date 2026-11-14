using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AppDomainMonitoringContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MonitoringUsesAvailableMetricsAndRejectsUnavailableOnes(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("DomainMonitoringFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "DomainMonitoringFixture.EntryPoint",
            "Run", [new("run", "DomainMonitoringFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        namespace DomainMonitoringFixture;
        public static class EntryPoint
        {
            private static byte[]? retained;
            public static int Run(int input)
            {
                if (Portable(input) != 42) return 20;
                // Survived memory is upstream's: the collector's heap less its fragmentation, never negative.
                if (AppDomain.MonitoringSurvivedProcessMemorySize < 0 ||
                    AppDomain.CurrentDomain.MonitoringSurvivedMemorySize != AppDomain.MonitoringSurvivedProcessMemorySize) return 21;
                // As upstream's WASI build reports: no processor-time clock.
                if (AppDomain.CurrentDomain.MonitoringTotalProcessorTime != TimeSpan.Zero) return 23;
                if (!AppDomain.MonitoringIsEnabled) return 22;
                return input + 1;
            }
            public static int Portable(int input)
            {
                if (!AppDomain.MonitoringIsEnabled) return 1;
                AppDomain.MonitoringIsEnabled = true;
                try { AppDomain.MonitoringIsEnabled = false; return 2; } catch (ArgumentException) { }
                if (!AppDomain.MonitoringIsEnabled) return 3;
                var before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                retained = new byte[2048]; retained[0] = 42;
                var after = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                if (before < 0 || after < before || retained[0] != 42) return 4;
                return input + 1;
            }
        }
        """;
}
