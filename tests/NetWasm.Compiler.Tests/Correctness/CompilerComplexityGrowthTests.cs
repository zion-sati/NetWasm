using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CompilerComplexityGrowthTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void FiveSizesOfEveryGrowthFamilyRemainStructurallyBounded()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var fixtures = services.GetRequiredService<IComplexityGrowthFixtureFactory>()
            .Create();
        var compiler = services.GetRequiredService<IRoslynCorpusCompiler>();
        var desktopRunner = services.GetRequiredService<IDesktopOracleRunner>();
        var netWasmRunner = services.GetRequiredService<INetWasmOracleRunner>();
        var comparer = services.GetRequiredService<IOracleComparer>();
        var root = CorrectnessTestAssets.CreateDirectory();
        var reports = new List<(int Size, CompilerComplexityReport Report)>();

        foreach (var fixture in fixtures)
        {
            var directory = Path.Combine(root, fixture.Fixture.Name);
            var compilation = compiler.Compile(
                fixture.Fixture,
                CilProfile.Release,
                directory);
            var desktop = desktopRunner.Run(compilation, TestContext.Current.CancellationToken);
            var netWasm = netWasmRunner.CompileAndRun(
                compilation,
                WasmTarget.Wasm32, TestContext.Current.CancellationToken);
            Assert.True(comparer.Compare(desktop[3], netWasm.Observations[3]).Equivalent);
            var metricsPath = Path.Combine(
                netWasm.DiagnosticTracePath + ".passes",
                "06-complexity.json");
            var report = JsonSerializer.Deserialize<CompilerComplexityReport>(
                File.ReadAllText(metricsPath),
                SerializerOptions)
                ?? throw new InvalidOperationException("complexity metrics were empty");
            reports.Add((fixture.Size, report));
        }

        foreach (var methodName in new[]
                 {
                     "Diamond",
                     "SwitchFanIn",
                     "NestedFinally",
                     "Irreducible",
                     "<Async>",
                 })
        {
            var family = reports.Select(item => (
                item.Size,
                Metric: SingleMetric(item.Report, methodName))).ToArray();
            Assert.Equal(5, family.Length);
            Assert.All(family, item => AssertBounded(item.Size, item.Metric));
            Assert.True(
                family[^1].Metric.CilInstructionCount >
                family[0].Metric.CilInstructionCount,
                $"{methodName} CIL did not grow with its requested structure");
            Assert.True(
                family[^1].Metric.OriginalBlockCount >
                family[0].Metric.OriginalBlockCount,
                $"{methodName} CFG did not grow with its requested structure");
            Assert.True(
                family[^1].Metric.WasmBodyBytes > family[0].Metric.WasmBodyBytes,
                $"{methodName} Wasm did not grow with its requested structure");
        }

        var irreducible = reports.Select(item =>
            SingleMetric(item.Report, "Irreducible")).ToArray();
        Assert.All(irreducible, metric => Assert.True(metric.SyntheticBlockCount > 0));
        var cleanup = reports.Select(item =>
            SingleMetric(item.Report, "NestedFinally")).ToArray();
        Assert.All(cleanup, metric =>
            Assert.Equal(metric.ReachableOriginalBlockCount,
                metric.OriginalBodyEmissionCount));
    }

    private static CompilerMethodComplexityMetric SingleMetric(
        CompilerComplexityReport report,
        string methodName)
    {
        var matches = report.Methods.Where(metric =>
            metric.Identity.Contains(methodName, StringComparison.Ordinal) &&
            (methodName != "<Async>" ||
             metric.Identity.Contains("MoveNext", StringComparison.Ordinal)))
            .ToArray();
        return Assert.Single(matches);
    }

    private static void AssertBounded(
        int requestedSize,
        CompilerMethodComplexityMetric metric)
    {
        Assert.Empty(metric.MissingOriginalBlocks);
        Assert.Empty(metric.DuplicatedOriginalBlocks);
        Assert.Equal(
            metric.ReachableOriginalBlockCount,
            metric.OriginalBodyEmissionCount);
        Assert.InRange(metric.OriginalBlockCount, 1, requestedSize * 16 + 32);
        Assert.InRange(metric.CfgEdgeCount, 0, requestedSize * 32 + 64);
        Assert.InRange(
            metric.IrBlockCount,
            metric.ReachableOriginalBlockCount,
            metric.OriginalBlockCount * 8 + metric.CfgEdgeCount * 4 + 32);
        Assert.InRange(
            metric.WasmInstructionCount,
            1,
            metric.CilInstructionCount * 128 + metric.CfgEdgeCount * 64 + 512);
        Assert.InRange(
            metric.WasmBodyBytes,
            1,
            metric.WasmInstructionCount * 16 + 4_096);
        Assert.True(metric.CompileDurationTicks > 0);
        Assert.True(metric.PeakObservedManagedMemoryBytes > 0);
    }
}
