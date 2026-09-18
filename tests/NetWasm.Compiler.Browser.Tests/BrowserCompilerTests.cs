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
        var preparations = new FixedPreparationFactory();
        var transport = new RecordingTransport();
        var owner = new RecordingDisposable();
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());
        using (var session = new BrowserCompilerSession(owner, factory, preparations,
                   transport, command))
            Assert.Same(expected, session.Compile(request));

        Assert.True(owner.Disposed);
        Assert.Null(state.Active);
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(null!, factory,
            preparations, transport, command));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(owner, null!,
            preparations, transport, command));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(owner, factory,
            null!, transport, command));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(owner, factory,
            preparations, null!, command));
        Assert.Throws<ArgumentNullException>(() => new BrowserCompilerSession(owner, factory,
            preparations, transport, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(null!, factory, preparations, transport, command));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(owner, null!, preparations, transport, command));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(owner, factory, null!, transport, command));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(owner, factory, preparations, null!, command));
        Assert.Throws<ArgumentNullException>(() =>
            new BrowserCompilerComposition(owner, factory, preparations, transport, null!));
    }

    [Fact]
    public void PreparedCompilationHasOneBoundedLoadCompilePublishLifetime()
    {
        var state = new BrowserCompilationRequestState();
        var factory = new BrowserCompilationRequestFactory(state);
        var command = new FixedBrowserCompilationCommand(
            new BrowserCompilationResult([], 0, [], [], null!, null!));
        var descriptor = new FrontendArtifactCacheDescriptor(
            "frontend-artifact-cache-v4", new string('a', 64), new string('d', 32));
        var publication = new FrontendArtifactCachePublication(new string('e', 32), 1, 1);
        var transport = new RecordingTransport(descriptor, publication);
        using var session = new BrowserCompilerSession(new RecordingDisposable(), factory,
            new FixedPreparationFactory(), transport, command);
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());

        var preparation = session.Prepare(request);

        Assert.Equal(descriptor, preparation.FrontendCache);
        Assert.Null(state.Active);
        Assert.True(transport.PreparedOptions!.EnableFrontendCache);
        Assert.Throws<InvalidOperationException>(() => session.Prepare(request));
        Assert.Throws<InvalidOperationException>(() => session.Compile(request));
        Assert.Throws<InvalidOperationException>(() => session.CompilePrepared("stale", []));
        var loaded = new FrontendArtifactCacheEntry(new string('c', 64), [1], [2]);

        var result = session.CompilePrepared(preparation.Handle, [loaded]);

        Assert.Same(command.Result, result.Compilation);
        Assert.Equal(publication, result.FrontendPublication);
        Assert.Equal(descriptor, transport.ImportedDescriptor);
        Assert.Equal([loaded], transport.ImportedEntries);
        Assert.True(command.Preparation!.Options.EnableFrontendCache);
        Assert.Null(state.Active);
        Assert.Throws<InvalidOperationException>(() =>
            session.CompilePrepared(preparation.Handle, []));
        Assert.Throws<InvalidOperationException>(() => session.Prepare(request));
        var batch = session.ReadFrontendArtifactBatch(publication);
        session.AcknowledgeFrontendArtifactBatch(publication, batch);
        var cancelled = session.Prepare(request);
        session.CancelPrepared(cancelled.Handle);
        Assert.Throws<InvalidOperationException>(() =>
            session.CompilePrepared(cancelled.Handle, []));
    }

    [Fact]
    public void PreparedCompilationPreservesSuccessWhenPublicationFails()
    {
        var state = new BrowserCompilationRequestState();
        var expected = new BrowserCompilationResult([], 0, [], [], null!, null!);
        var command = new FixedBrowserCompilationCommand(expected);
        var descriptor = new FrontendArtifactCacheDescriptor(
            "frontend-artifact-cache-v4", new string('a', 64), new string('d', 32));
        var transport = new RecordingTransport(descriptor, failCompletion: true);
        using var session = new BrowserCompilerSession(new RecordingDisposable(),
            new BrowserCompilationRequestFactory(state), new FixedPreparationFactory(),
            transport, command);
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]>(), new Dictionary<string, string>());
        var preparation = session.Prepare(request);

        var result = session.CompilePrepared(preparation.Handle,
            [new(new string('b', 64), [1], [2])]);

        Assert.Same(expected, result.Compilation);
        Assert.Null(result.FrontendPublication);
        Assert.Null(state.Active);
        var recovery = session.Prepare(request);
        session.CancelPrepared(recovery.Handle);
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
        public BrowserCompilationResult Result { get; } = result;
        public PreparedBrowserCompilation? Preparation { get; private set; }
        public BrowserCompilationResult Compile(PreparedBrowserCompilation preparation)
        {
            Preparation = preparation;
            return Result;
        }
    }

    private sealed class FixedPreparationFactory : IBrowserCompilationPreparationFactory
    {
        public PreparedBrowserCompilation Prepare(BrowserCompilationRequest request) =>
            new(request, request.Options, null);
    }

    private sealed class RecordingTransport(
        FrontendArtifactCacheDescriptor? descriptor = null,
        FrontendArtifactCachePublication? publication = null,
        bool failCompletion = false) :
        IFrontendArtifactCacheTransport
    {
        public CompilerOptions? PreparedOptions { get; private set; }
        public FrontendArtifactCacheDescriptor? ImportedDescriptor { get; private set; }
        public IReadOnlyList<FrontendArtifactCacheEntry>? ImportedEntries { get; private set; }
        public bool Abandoned { get; private set; }
        public FrontendArtifactCacheDescriptor? Prepare(CompilerOptions options)
        {
            PreparedOptions = options;
            return descriptor;
        }
        public IDisposable BeginCompilation(FrontendArtifactCacheDescriptor descriptor,
            IReadOnlyList<FrontendArtifactCacheEntry> entries)
        {
            ImportedDescriptor = descriptor;
            ImportedEntries = entries;
            return new RecordingDisposable();
        }
        public FrontendArtifactCachePublication? CompleteCompilation(
            FrontendArtifactCacheDescriptor descriptor) => failCompletion
            ? throw new IOException("fixture") : publication;
        public void CancelPreparation(FrontendArtifactCacheDescriptor descriptor) { }
        public FrontendArtifactCacheBatch ReadBatch(
            FrontendArtifactCachePublication publication) =>
            new(publication.Token, new string('f', 32), [], true);
        public void AcknowledgeBatch(FrontendArtifactCachePublication publication,
            FrontendArtifactCacheBatch batch) { }
        public void Abandon(FrontendArtifactCachePublication publication) => Abandoned = true;
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
