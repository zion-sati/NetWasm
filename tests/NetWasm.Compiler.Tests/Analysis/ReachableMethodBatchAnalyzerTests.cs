using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ReachableMethodBatchAnalyzerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PublishesResultsInRequestOrderAndObservesTheCompletedBatch(bool synchronous)
    {
        var requests = CreateRequests();
        var results = requests.Select(CreateAnalysis).ToArray();
        var calls = new List<ReachableMethodRequest>();
        var observer = new RecordingObserver();
        var analyzer = new RecordingAnalyzer(request =>
        {
            calls.Add(request);
            return results[calls.Count - 1];
        });
        var batch = CreateBatch(synchronous, analyzer, observer);

        var actual = batch.Analyze(requests).ToArray();

        Assert.Equal(results, actual);
        Assert.Equal(requests, calls);
        for (var index = 0; index < requests.Length; index++)
        {
            Assert.Same(results[index], actual[index]);
            Assert.Same(requests[index], calls[index]);
        }
        var observation = Assert.Single(observer.Observations);
        Assert.Equal(requests.Length, observation.RequestCount);
        Assert.Equal(1, observation.WorkerCount);
        Assert.True(observation.Elapsed >= TimeSpan.Zero);
        Assert.True(observation.WorkerBusy >= TimeSpan.Zero);
        if (synchronous)
        {
            Assert.Equal(TimeSpan.Zero, observation.OrderedWait);
            Assert.Equal(0, observation.PeakBufferedResults);
        }
    }

    [Fact]
    public void SynchronousAnalysisUsesTheEnumeratingThread()
    {
        var threadId = Environment.CurrentManagedThreadId;
        var analyzer = new RecordingAnalyzer(request =>
        {
            Assert.Equal(threadId, Environment.CurrentManagedThreadId);
            return CreateAnalysis(request);
        });
        var batch = CreateBatch(true,
            analyzer,
            new RecordingObserver());

        Assert.Equal(3, batch.Analyze(CreateRequests()).Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsAnUninitializedBatchBeforeCallingCollaborators(bool synchronous)
    {
        var calls = 0;
        var observer = new RecordingObserver();
        var batch = CreateBatch(synchronous, new RecordingAnalyzer(request =>
        {
            calls++;
            return CreateAnalysis(request);
        }), observer);

        var failure = Assert.Throws<ArgumentException>(() => batch.Analyze(default));

        Assert.Equal("requests", failure.ParamName);
        Assert.Equal(0, calls);
        Assert.Empty(observer.Observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyAndUnenumeratedBatchesDoNotCallCollaborators(bool synchronous)
    {
        var calls = 0;
        var observer = new RecordingObserver();
        var batch = CreateBatch(synchronous, new RecordingAnalyzer(request =>
        {
            calls++;
            return CreateAnalysis(request);
        }), observer);

        Assert.Empty(batch.Analyze([]));
        _ = batch.Analyze(CreateRequests());

        Assert.Equal(0, calls);
        Assert.Empty(observer.Observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FinishesTheBatchAndObservesWhenEnumerationStopsEarly(bool synchronous)
    {
        var calls = 0;
        var observer = new RecordingObserver();
        var batch = CreateBatch(synchronous, new RecordingAnalyzer(request =>
        {
            Interlocked.Increment(ref calls);
            return CreateAnalysis(request);
        }), observer);

        using (var enumerator = batch.Analyze(CreateRequests()).GetEnumerator())
        {
            Assert.True(enumerator.MoveNext());
            Assert.Empty(observer.Observations);
        }

        Assert.Equal(3, calls);
        Assert.Single(observer.Observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PublishesTheFirstFailureInOrderWithItsCauseAndFinishesTheBatch(bool synchronous)
    {
        var requests = CreateRequests();
        var calls = 0;
        var firstFailure = new InvalidOperationException("First analysis failed.");
        var laterFailure = new ArgumentException("Later analysis failed.");
        var observer = new RecordingObserver();
        var batch = CreateBatch(synchronous, new RecordingAnalyzer(request =>
        {
            Interlocked.Increment(ref calls);
            if (ReferenceEquals(request, requests[1]))
            {
                return ThrowAnalysisFailure(firstFailure);
            }

            if (ReferenceEquals(request, requests[2]))
            {
                return ThrowAnalysisFailure(laterFailure);
            }

            return CreateAnalysis(request);
        }), observer);
        using var enumerator = batch.Analyze(requests).GetEnumerator();

        Assert.True(enumerator.MoveNext());
        var actual = Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());

        Assert.Same(firstFailure, actual);
        Assert.Contains(nameof(ThrowAnalysisFailure), actual.StackTrace);
        Assert.Equal(3, calls);
        Assert.Single(observer.Observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsAMissingAnalysisAndStillObservesTheBatch(bool synchronous)
    {
        var calls = 0;
        var observer = new RecordingObserver();
        var batch = CreateBatch(synchronous, new RecordingAnalyzer(_ =>
        {
            Interlocked.Increment(ref calls);
            return null!;
        }), observer);

        var failure = Assert.Throws<InvalidOperationException>(() => batch.Analyze(CreateRequests()).ToArray());

        Assert.Equal("A reachable-method analyzer did not publish a result.", failure.Message);
        Assert.Equal(3, calls);
        Assert.Single(observer.Observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PropagatesObserverFailureAfterCompletingTheBatch(bool synchronous)
    {
        var failure = new InvalidOperationException("Observation failed.");
        var observer = new RecordingObserver(failure);
        var batch = CreateBatch(synchronous, new RecordingAnalyzer(CreateAnalysis), observer);

        var actual = Assert.Throws<InvalidOperationException>(() => batch.Analyze(CreateRequests()).ToArray());

        Assert.Same(failure, actual);
        Assert.Single(observer.Observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ObserverFailureTakesPrecedenceOverAnAnalysisFailure(bool synchronous)
    {
        var analysisFailure = new ArgumentException("Analysis failed.");
        var observerFailure = new InvalidOperationException("Observation failed.");
        var observer = new RecordingObserver(observerFailure);
        var batch = CreateBatch(synchronous,
            new RecordingAnalyzer(_ => ThrowAnalysisFailure(analysisFailure)), observer);

        var actual = Assert.Throws<InvalidOperationException>(() => batch.Analyze(CreateRequests()).ToArray());

        Assert.Same(observerFailure, actual);
        Assert.Single(observer.Observations);
    }

    [Fact]
    public void SynchronousStrategyRequiresItsCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new SynchronousReachableMethodBatchAnalyzer(
            null!, new RecordingObserver()));
        Assert.Throws<ArgumentNullException>(() => new SynchronousReachableMethodBatchAnalyzer(
            new RecordingAnalyzer(CreateAnalysis), null!));
    }

    private static IReachableMethodBatchAnalyzer CreateBatch(
        bool synchronous,
        IReachableMethodAnalyzer analyzer,
        IReachableMethodBatchObserver observer) => synchronous
        ? new SynchronousReachableMethodBatchAnalyzer(analyzer, observer)
        : new ReachableMethodBatchAnalyzer([analyzer], observer);

    // Method bodies are opaque payloads to the batch contract. These distinct
    // requests/results exercise identity and ordering without invoking compilation.
    private static ImmutableArray<ReachableMethodRequest> CreateRequests() =>
        [new(null!), new(null!), new(null!)];

    private static ReachableMethodAnalysis CreateAnalysis(ReachableMethodRequest request) =>
        new(request.Method, null!, [], [], new([], [], [], [], [], [], [], [], [], []));

    private static ReachableMethodAnalysis ThrowAnalysisFailure(Exception failure) => throw failure;

    private sealed class RecordingAnalyzer(
        Func<ReachableMethodRequest, ReachableMethodAnalysis> analyze) : IReachableMethodAnalyzer
    {
        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request) => analyze(request);
    }

    private sealed class RecordingObserver(Exception? failure = null) : IReachableMethodBatchObserver
    {
        public List<ReachableMethodBatchObservation> Observations { get; } = [];

        public void Observe(ReachableMethodBatchObservation observation)
        {
            Observations.Add(observation);
            if (failure is { } exception)
            {
                throw exception;
            }
        }
    }
}
