using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsMetricsGenericCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerKeepsGenericMetricInstrumentsDistinct(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Diagnostics.Metrics;

            namespace DiagnosticsMetricsGenericFixture;

            public static class EntryPoint
            {
                private static int s_publications;
                private static int s_measurements;

                public static int Run(int input)
                {
                    s_publications = 0;
                    s_measurements = 0;

                    using var meter = new Meter("fixture");
                    using var listener = new MeterListener();
                    listener.InstrumentPublished = static (instrument, current) =>
                    {
                        s_publications++;
                        current.EnableMeasurementEvents(instrument);
                    };
                    listener.SetMeasurementEventCallback<int>(
                        static (_, measurement, _, _) =>
                            s_measurements += measurement);
                    listener.Start();

                    meter.CreateCounter<int>("counter").Add(7);
                    meter.CreateGauge<int>("gauge").Record(11);

                    return s_publications == 2 && s_measurements == 18
                        ? 18
                        : 0;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("DiagnosticsMetricsGenericFixture", source)
            : assets.CompileSource("DiagnosticsMetricsGenericFixture", source);
        var compilation = NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "DiagnosticsMetricsGenericFixture.EntryPoint",
                "Run",
                [new RequestedExport("run", "DiagnosticsMetricsGenericFixture.EntryPoint", "Run")],
                target,
                WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
                WitWorld: "netwasm:platform/platform@1.0.0",
                UseJavaScriptExportBoundary: true));

        var observed = CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd);

        Assert.Equal(18, observed);
    }
}
