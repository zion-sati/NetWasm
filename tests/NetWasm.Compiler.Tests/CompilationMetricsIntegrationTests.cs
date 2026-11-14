using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationMetricsIntegrationTests
{
    [Fact]
    public void SingleWorkerCompilationExercisesTheSerialPipeline()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "SingleWorkerCompilation",
            """
            namespace SingleWorkerCompilation;

            public static class EntryPoint
            {
                public static T Identity<T>(T value) => value;
                public static int Run(int value) => Identity(value);
            }
            """);
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton(new CompilerParallelism(1));
        using var provider = services.BuildServiceProvider();
        var options = new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "SingleWorkerCompilation.EntryPoint",
            "Run",
            []);

        var result = provider.GetRequiredService<INetWasmCompiler>()
            .Compile(options);

        Assert.NotEmpty(result.ApplicationModule);
    }

    [Fact]
    public void RealCompilationReportsEveryStageAndReconcilesTotal()
    {
        using var assets = TestAssets.Create();
        var services = new ServiceCollection().AddNetWasmCompiler();
        var progress = new RecordingProgressReporter();
        services.AddSingleton<ICompilerProgressReporter>(progress);
        using var provider = services.BuildServiceProvider();
        var observer = new RecordingObserver();
        var options = CompilerTestSupport.Options(assets, []) with
        {
            MetricsObserver = observer,
        };

        var result = provider.GetRequiredService<INetWasmCompiler>().Compile(options);

        Assert.NotEmpty(result.ApplicationModule);
        var report = Assert.Single(observer.Reports);
        Assert.Equal(CompilerMetricsOutcome.Succeeded, report.Outcome);
        Assert.Null(report.FailedStage);
        Assert.Equal(
            Enum.GetValues<CompilerMetricStage>().Where(stage =>
                stage != CompilerMetricStage.FailureReporting),
            report.Stages.Select(stage => stage.Stage));
        Assert.Equal(
            report.StageDuration,
            report.Stages.Aggregate(TimeSpan.Zero, (total, stage) => total + stage.Duration));
        Assert.InRange(
            Math.Abs(report.UnattributedDuration.Ticks),
            0,
            TimeSpan.FromMilliseconds(1).Ticks);
        Assert.Equal(report.TotalDuration - report.StageDuration, report.UnattributedDuration);
        Assert.True(report.Stages.Single(stage =>
            stage.Stage == CompilerMetricStage.LoweringAndEmission)
            .Counters["moduleBytes"] > 0);
        Assert.Equal(Enum.GetValues<CompilerProgressStage>(), progress.Stages);
    }

    [Fact]
    public void FailedCompilationReportsCompletedPrefixAndFailureWork()
    {
        using var assets = TestAssets.Create();
        using var provider = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider();
        var observer = new RecordingObserver();
        var options = CompilerTestSupport.Options(assets, []) with
        {
            EntryTypeName = "Missing.Entry.Type",
            MetricsObserver = observer,
        };

        Assert.ThrowsAny<Exception>(() =>
            provider.GetRequiredService<INetWasmCompiler>().Compile(options));

        var report = Assert.Single(observer.Reports);
        Assert.Equal(CompilerMetricsOutcome.Failed, report.Outcome);
        Assert.Equal("entry", report.FailedStage);
        Assert.Equal(CompilerMetricStage.FailureReporting, report.Stages[^1].Stage);
        Assert.Equal(
            report.StageDuration,
            report.Stages.Aggregate(TimeSpan.Zero, (total, stage) => total + stage.Duration));
        Assert.InRange(
            Math.Abs(report.UnattributedDuration.Ticks),
            0,
            TimeSpan.FromMilliseconds(1).Ticks);
    }

    [Fact]
    public void CanceledCompilationReportsCanceledOutcome()
    {
        using var assets = TestAssets.Create();
        var services = new ServiceCollection().AddNetWasmCompiler();
        services.AddSingleton<IMetadataCompilationLoader>(new CancelingMetadataLoader());
        using var provider = services.BuildServiceProvider();
        var observer = new RecordingObserver();
        var options = CompilerTestSupport.Options(assets, []) with
        {
            MetricsObserver = observer,
        };

        Assert.Throws<OperationCanceledException>(() =>
            provider.GetRequiredService<INetWasmCompiler>().Compile(options));

        var report = Assert.Single(observer.Reports);
        Assert.Equal(CompilerMetricsOutcome.Canceled, report.Outcome);
        Assert.Equal("metadata", report.FailedStage);
        Assert.Equal(CompilerMetricStage.FailureReporting, report.Stages[^1].Stage);
    }

    private sealed class RecordingObserver : ICompilerMetricsObserver
    {
        public List<CompilerMetricsReport> Reports { get; } = [];

        public void Report(CompilerMetricsReport report) => Reports.Add(report);
    }

    private sealed class RecordingProgressReporter : ICompilerProgressReporter
    {
        public List<CompilerProgressStage> Stages { get; } = [];

        public void Report(CompilerProgressStage stage) => Stages.Add(stage);
    }

    private sealed class CancelingMetadataLoader : IMetadataCompilationLoader
    {
        IMetadataCompilationLease IMetadataCompilationLoader.Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths,
            ImmutableDictionary<string, string>? aliases,
            IEnumerable<string>? metadataReferencePaths,
            IEnumerable<string>? originalContractReferencePaths,
            NetWasm.Compiler.Core.NativeInterop.INativeImportBinder? nativeBinding) =>
            throw new OperationCanceledException();
    }
}
