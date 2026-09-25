using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class DateRangeBoundaryTests
{
    public static TheoryData<bool, WasmTarget, int> Cells
    {
        get
        {
            var cells = new TheoryData<bool, WasmTarget, int>();
            foreach (var optimize in new[] { false, true })
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            for (var input = 0; input < 5; input++) cells.Add(optimize, target, input);
            return cells;
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void CalendarOffsetAndTickBoundariesPreserveValuesAndManagedFailures(bool optimize, WasmTarget target, int input)
    {
        using var assets = TestAssets.Create();
        using var stream = typeof(DateRangeBoundaryTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/DateRange.cs.txt") ?? throw new InvalidOperationException("Date fixture resource missing.");
        using var reader = new StreamReader(stream);
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "DateRangeBoundaries", reader.ReadToEnd(), "NetWasm.Correctness.DateRangeBoundaries.EntryPoint", optimize, target, input, [])
        {
            Exports = [new RequestedExport("run", "NetWasm.Correctness.DateRangeBoundaries.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/platform",
        });
        Assert.Equal(42, observed);
    }

    // Reachable timezone branches require the platform contract even for UTC
    // inputs. These fixtures belong to the existing WIT-aware scenario runner.
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ExactDateTextPreservesUtcKindAndRejectsInvalidCalendarAndShortBuffers(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Globalization;
            namespace DateTextBoundary;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var value = new DateTime(2000, 2, 29, 23, 59, 59, DateTimeKind.Utc).AddTicks(1234567);
                    var text = value.ToString("O", CultureInfo.InvariantCulture);
                    if (text != "2000-02-29T23:59:59.1234567Z") return 1;
                    if (!DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return 2;
                    if (parsed.Ticks != value.Ticks || parsed.Kind != DateTimeKind.Utc) return 3;
                    if (DateTime.TryParseExact("1900-02-29T00:00:00.0000000Z", "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)) return 4;
                    var buffer = new char[1];
                    if (value.TryFormat(buffer, out var written, "O", CultureInfo.InvariantCulture) || written != 0) return 5;
                    return input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "DateTextBoundary", source, "DateTextBoundary.EntryPoint", optimize, target, 42, [])
        {
            Exports = [new RequestedExport("run", "DateTextBoundary.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform@1.0.0/platform",
        });
        Assert.Equal(42, observed);
    }
}
