using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The RFC 1123 format ("R" and "r") that HTTP dates use: a DateTime formats as given, a DateTimeOffset converts to
// UTC first, always with invariant names. The expected values are desktop .NET 11.0.0's.
public sealed class DateTimeRfc1123CompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void Rfc1123MatchesDotNet11(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "DateTimeRfc1123Fixture", Source, "DateTimeRfc1123Fixture.EntryPoint", false, target, 41, [])
        {
            Optimization = FinalWasmOptimization.None,
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            Exports = [new RequestedExport("run", "DateTimeRfc1123Fixture.EntryPoint", "Run")],
        }));
    }

    private const string Source = """
        using System;
        using System.Globalization;
        namespace DateTimeRfc1123Fixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (new DateTime(2026, 11, 11, 10, 5, 7).ToString("r") != "Wed, 11 Nov 2026 10:05:07 GMT") return 1;
                if (new DateTime(2026, 11, 11, 10, 5, 7, DateTimeKind.Local).ToString("R") != "Wed, 11 Nov 2026 10:05:07 GMT") return 2;
                if (new DateTimeOffset(2026, 11, 11, 10, 5, 7, TimeSpan.FromHours(10)).ToString("R") != "Wed, 11 Nov 2026 00:05:07 GMT") return 3;
                if (new DateTimeOffset(2026, 1, 3, 23, 59, 59, TimeSpan.FromHours(-5)).ToString("r", CultureInfo.InvariantCulture) != "Sun, 04 Jan 2026 04:59:59 GMT") return 4;
                return input + 1;
            }
        }
        """;
}
