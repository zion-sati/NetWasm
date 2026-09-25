using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusBuildExecutorTests
{
    [Fact]
    public void CompositionResolvesBothCapabilitiesAndPreservesInjectedProcessOwnership()
    {
        var process = new RecordingProcess();
        var environment = new CompilerCorrectnessEnvironment(
            Path.GetFullPath("repository"), "dotnet", "sdk", "roslyn", "corelib",
            "references", "oracle.mjs", "oracle.dll", "compiler.dll", TimeSpan.FromSeconds(1));
        var services = new ServiceCollection()
            .AddSingleton(environment)
            .AddSingleton<IQualifiedProcessRunner>(process);
        Assert.Same(services, services.AddLinkedCorpusBuild());
        using var provider = services.BuildServiceProvider();
        var plan = provider.GetRequiredService<ILinkedCorpusBuildPlanFactory>().Create(
            LinkedCorpusBuildPlanFactoryTests.Request);

        var receipts = provider.GetRequiredService<ILinkedCorpusBuildExecutor>().Execute(plan);

        Assert.Equal(3, receipts.Length);
        Assert.Equal(plan.Steps.Select(step => step.Process), process.Requests);
        Assert.IsType<LinkedCorpusObservationRequestWriter>(
            provider.GetRequiredService<ILinkedCorpusObservationRequestWriter>());
        Assert.IsType<LinkedCorpusObservationResponseParser>(
            provider.GetRequiredService<ILinkedCorpusObservationResponseParser>());
        Assert.IsType<LinkedCorpusObservationResponseReader>(
            provider.GetRequiredService<ILinkedCorpusObservationResponseReader>());
        Assert.IsType<LinkedCorpusObservationProcess>(
            provider.GetRequiredService<ILinkedCorpusObservationProcess>());
        Assert.IsType<LinkedCorpusToolPathsProvider>(
            provider.GetRequiredService<ILinkedCorpusToolPathsProvider>());
        Assert.IsType<CorpusArtifactFingerprint>(
            provider.GetRequiredService<ICorpusArtifactFingerprint>());
        Assert.IsType<LinkedCorpusObservationRequestFactory>(
            provider.GetRequiredService<ILinkedCorpusObservationRequestFactory>());
        Assert.Throws<ArgumentNullException>(() => LinkedCorpusServiceCollectionExtensions.AddLinkedCorpusBuild(null!));
    }

    [Fact]
    public void ExecutesEveryStageInOrderAndRetainsExactProcessReceipts()
    {
        var process = new RecordingProcess();
        var executor = new LinkedCorpusBuildExecutor(process);
        var plan = Plan;
        using var cancellation = new CancellationTokenSource();

        var results = ((ILinkedCorpusBuildExecutor)executor).Execute(plan, cancellation.Token);

        Assert.Equal(plan.Steps.Select(step => step.Process), process.Requests);
        Assert.Equal(plan.Steps, results.Select(result => result.Step));
        Assert.All(results, result => Assert.Same(process.Success, result.Result));
        Assert.All(process.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FirstNonzeroStageStopsThePlanWithoutRetryAndRetainsPrecedingReceipts(int failedStage)
    {
        var failure = new QualifiedProcessResult(QualifiedProcessCompletion.Exited, 1, "output", "failure", TimeSpan.FromSeconds(1));
        var process = new RecordingProcess { FailedStage = failedStage, Failure = failure };
        var executor = new LinkedCorpusBuildExecutor(process);
        var plan = Plan;

        var error = Assert.Throws<LinkedCorpusBuildException>(() => executor.Execute(plan));

        Assert.Equal(plan.Steps[failedStage].Stage, error.Stage);
        Assert.Equal(failedStage + 1, process.Requests.Count);
        Assert.Equal(failedStage + 1, error.Completed.Length);
        Assert.Same(failure, error.Completed[^1].Result);
        Assert.Null(error.InnerException);
        Assert.Equal(plan.Steps.Take(failedStage + 1), error.Completed.Select(result => result.Step));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TimeoutAndLaunchFailureStopBeforeLinking(bool timeout)
    {
        var cause = new IOException("launch failed");
        var failure = new QualifiedProcessResult(
            timeout ? QualifiedProcessCompletion.TimedOut : QualifiedProcessCompletion.LaunchFailed,
            null, "", "", TimeSpan.Zero) { LaunchException = cause };
        var process = new RecordingProcess { FailedStage = 0, Failure = failure };
        var executor = new LinkedCorpusBuildExecutor(process);

        var error = Assert.Throws<LinkedCorpusBuildException>(() => executor.Execute(Plan));

        Assert.Same(cause, error.InnerException);
        Assert.Same(failure, Assert.Single(error.Completed).Result);
        Assert.Single(process.Requests);
    }

    [Fact]
    public void CancellationBeforeOrBetweenStagesStopsFurtherProcesses()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var process = new RecordingProcess();
        var executor = new LinkedCorpusBuildExecutor(process);
        Assert.Throws<OperationCanceledException>(() => executor.Execute(Plan, cancellation.Token));
        Assert.Empty(process.Requests);

        using var between = new CancellationTokenSource();
        process.AfterRun = between.Cancel;
        Assert.Throws<OperationCanceledException>(() => executor.Execute(Plan, between.Token));
        Assert.Single(process.Requests);
    }

    [Fact]
    public void ProcessExceptionPreservesCauseAndDoesNotRetry()
    {
        var cause = new InvalidOperationException("process contract failed");
        var process = new RecordingProcess { Throw = cause };
        var executor = new LinkedCorpusBuildExecutor(process);
        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => executor.Execute(Plan)));
        Assert.Single(process.Requests);
    }

    [Fact]
    public void InvalidDependenciesAndEmptyPlansAreRejectedBeforeExecution()
    {
        Assert.Throws<ArgumentNullException>(() => new LinkedCorpusBuildExecutor(null!));
        var process = new RecordingProcess();
        var executor = new LinkedCorpusBuildExecutor(process);
        Assert.Throws<ArgumentNullException>(() => executor.Execute(null!));
        Assert.Throws<ArgumentException>(() => executor.Execute(Plan with { Steps = [] }));
        Assert.Throws<ArgumentException>(() => executor.Execute(Plan with { Steps = default }));
        Assert.Empty(process.Requests);
    }

    private static LinkedCorpusBuildPlan Plan => new LinkedCorpusBuildPlanFactory().Create(
        LinkedCorpusBuildPlanFactoryTests.Request with { Form = CorpusWasmForm.Optimized });

    private sealed class RecordingProcess : IQualifiedProcessRunner
    {
        public List<QualifiedProcessRequest> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public QualifiedProcessResult Success { get; } = new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);
        public QualifiedProcessResult? Failure { get; init; }
        public int FailedStage { get; init; } = -1;
        public Action? AfterRun { get; set; }
        public Exception? Throw { get; init; }

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Tokens.Add(cancellationToken);
            if (Throw is not null)
                throw Throw;
            AfterRun?.Invoke();
            return Requests.Count - 1 == FailedStage ? Failure! : Success;
        }
    }
}
