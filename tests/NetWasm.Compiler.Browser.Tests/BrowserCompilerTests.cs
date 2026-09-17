namespace NetWasm.Compiler.Browser.Tests;

using NetWasm.Compiler.Diagnostics;

public sealed class BrowserCompilerTests
{
    [Fact]
    public void RequiresARequest()
    {
        Assert.Throws<ArgumentNullException>(() => BrowserCompiler.Compile(null!));
    }

    [Fact]
    public void AReusableSessionReleasesFailedRequestsAndRejectsUseAfterDisposal()
    {
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());
        var session = new BrowserCompilerSession();

        Assert.Throws<FileNotFoundException>(() => session.Compile(request));
        Assert.Throws<FileNotFoundException>(() => session.Compile(request));
        session.Dispose();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Compile(request));
    }

    [Fact]
    public void AReusableSessionDelegatesSuccessfulRequestsAndRequiresItsActors()
    {
        var state = new BrowserCompilationRequestState();
        var factory = new BrowserCompilationRequestFactory(state);
        var expected = new BrowserCompilationResult([], 0, [], [], null!, null!);
        var command = new FixedBrowserCompilationCommand(expected);
        var owner = new RecordingDisposable();
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());
        using (var session = new BrowserCompilerSession(owner, factory, command))
            Assert.Same(expected, session.Compile(request));

        Assert.True(owner.Disposed);
        Assert.Null(state.Active);
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(null!, factory, command));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(owner, null!, command));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(owner, factory, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(null!, factory, command));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(owner, null!, command));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(owner, factory, null!));
    }

    [Fact]
    public void TheComposedCompilerFailsClosedOnAnUnsuppliedEntryAssembly()
    {
        var observer = new RecordingMetricsObserver();
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions() with
        { MetricsObserver = observer },
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());

        var failure = Assert.Throws<FileNotFoundException>(() => BrowserCompiler.Compile(request));

        Assert.Equal("app.dll", failure.FileName);
        var metrics = Assert.Single(observer.Reports);
        Assert.Equal(CompilerMetricsOutcome.Failed, metrics.Outcome);
        Assert.Equal("metadata", metrics.FailedStage);
        Assert.Equal(CompilerMetricStage.FailureReporting, metrics.Stages[^1].Stage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitMetricsSelectionPreservesCompilerFailure(bool collectMetrics)
    {
        var request = new BrowserCompilationRequest(
            BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, string>())
        {
            CollectCompilerMetrics = collectMetrics,
        };

        var failure = Assert.Throws<FileNotFoundException>(() => BrowserCompiler.Compile(request));

        Assert.Equal("app.dll", failure.FileName);
    }

    [Fact]
    public void MetricsObserverCapturesAndForwardsTheSameReport()
    {
        var inner = new RecordingMetricsObserver();
        var observer = new BrowserCompilerMetricsObserver(inner);
        var report = new CompilerMetricsReport(
            CompilerMetricsOutcome.Succeeded,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            []);

        ((ICompilerMetricsObserver)observer).Report(report);

        Assert.Same(report, observer.Report);
        Assert.Same(report, Assert.Single(inner.Reports));
        Assert.Throws<ArgumentNullException>(() =>
            ((ICompilerMetricsObserver)observer).Report(null!));
    }

    [Fact]
    public void MetricsObserverCanCaptureWithoutAForwardObserver()
    {
        var observer = new BrowserCompilerMetricsObserver(null);
        var report = new CompilerMetricsReport(
            CompilerMetricsOutcome.Canceled,
            "analysis",
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            []);

        ((ICompilerMetricsObserver)observer).Report(report);

        Assert.Same(report, observer.Report);
    }

    private sealed class RecordingMetricsObserver : ICompilerMetricsObserver
    {
        public List<CompilerMetricsReport> Reports { get; } = [];

        public void Report(CompilerMetricsReport report) => Reports.Add(report);
    }

    private sealed class FixedBrowserCompilationCommand(BrowserCompilationResult result) :
        IBrowserCompilationCommand
    {
        public BrowserCompilationResult Compile(BrowserCompilationRequest request) => result;
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
