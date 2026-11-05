using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class ReachableMethodBatchAnalyzerTests
{
    [Fact]
    public void SynchronousStrategyRequiresItsCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SynchronousReachableMethodBatchAnalyzer(
                null!, new RecordingReachableMethodBatchObserver()));
        Assert.Throws<ArgumentNullException>(() =>
            new SynchronousReachableMethodBatchAnalyzer(
                new DelegatingAnalyzer(CreateAnalysis), null!));
    }

    [Fact]
    public void SynchronousStrategyUsesTheEnumeratingThreadAndReportsTheBatch()
    {
        var threadId = Environment.CurrentManagedThreadId;
        var calls = 0;
        var observer = new RecordingReachableMethodBatchObserver();
        var analyzer = new SynchronousReachableMethodBatchAnalyzer(
            new DelegatingAnalyzer(request =>
            {
                Assert.Equal(threadId, Environment.CurrentManagedThreadId);
                calls++;
                return CreateAnalysis(request);
            }),
            observer);
        var requests = ImmutableArray.Create(
            new ReachableMethodRequest(CreateMethod()),
            new ReachableMethodRequest(CreateMethod()));

        var results = analyzer.Analyze(requests).ToArray();

        Assert.Equal(2, calls);
        Assert.Equal(requests.Select(request => request.Method),
            results.Select(result => result.Method));
        var observation = Assert.Single(observer.Observations);
        Assert.Equal(2, observation.RequestCount);
        Assert.Equal(1, observation.WorkerCount);
        Assert.Equal(TimeSpan.Zero, observation.OrderedWait);
        Assert.Equal(0, observation.PeakBufferedResults);
    }

    [Fact]
    public void SynchronousStrategyRejectsDefaultAndAcceptsEmptyBatches()
    {
        var analyzer = new SynchronousReachableMethodBatchAnalyzer(
            new DelegatingAnalyzer(CreateAnalysis),
            new RecordingReachableMethodBatchObserver());

        Assert.Throws<ArgumentException>(() =>
            analyzer.Analyze(default).ToArray());
        Assert.Empty(analyzer.Analyze([]));
    }

    [Fact]
    public void SynchronousStrategyCompletesTheBatchBeforePublishingTheFirstFailure()
    {
        var calls = 0;
        var observer = new RecordingReachableMethodBatchObserver();
        var expected = new InvalidOperationException("first request failed");
        var analyzer = new SynchronousReachableMethodBatchAnalyzer(
            new DelegatingAnalyzer(request =>
            {
                calls++;
                if (calls == 1) throw expected;
                return CreateAnalysis(request);
            }), observer);
        var requests = ImmutableArray.Create(
            new ReachableMethodRequest(CreateMethod()),
            new ReachableMethodRequest(CreateMethod()));

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            analyzer.Analyze(requests).ToArray()));
        Assert.Equal(2, calls);
        Assert.Single(observer.Observations);
    }

    [Fact]
    public void SynchronousStrategyRejectsAMissingAnalysisResult()
    {
        var analyzer = new SynchronousReachableMethodBatchAnalyzer(
            new DelegatingAnalyzer(_ => null!),
            new RecordingReachableMethodBatchObserver());

        Assert.Throws<InvalidOperationException>(() => analyzer.Analyze([
            new ReachableMethodRequest(CreateMethod())
        ]).ToArray());
    }

    [Fact]
    public void SynchronousStrategyFinishesTheBatchWhenEnumerationStopsEarly()
    {
        var calls = 0;
        var observer = new RecordingReachableMethodBatchObserver();
        var analyzer = new SynchronousReachableMethodBatchAnalyzer(
            new DelegatingAnalyzer(request =>
            {
                calls++;
                return CreateAnalysis(request);
            }),
            observer);
        var requests = ImmutableArray.Create(
            new ReachableMethodRequest(CreateMethod()),
            new ReachableMethodRequest(CreateMethod()));

        using (var enumerator = analyzer.Analyze(requests).GetEnumerator())
        {
            Assert.True(enumerator.MoveNext());
            Assert.Empty(observer.Observations);
        }

        Assert.Equal(2, calls);
        Assert.Single(observer.Observations);
    }

    [Fact]
    public void PoolRejectsMissingOrEmptyCollaborators()
    {
        var analyzer = new DelegatingAnalyzer(CreateAnalysis);
        var observer = new RecordingReachableMethodBatchObserver();
        Assert.Throws<ArgumentException>(() => new ReachableMethodAnalysisPool([], analyzer));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalysisPool([null!], analyzer));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalysisPool([analyzer], null!));
        Assert.Throws<ArgumentException>(() =>
            new PooledReachableMethodAnalysisSessionFactory([], analyzer, observer));
        Assert.Throws<ArgumentNullException>(() =>
            new PooledReachableMethodAnalysisSessionFactory([analyzer], null!, observer));
        Assert.Throws<ArgumentNullException>(() =>
            new PooledReachableMethodAnalysisSessionFactory([analyzer], analyzer, null!));
        using var pool = new ReachableMethodAnalysisPool([analyzer], analyzer);
        Assert.Throws<ArgumentNullException>(() => new PipelinedReachableMethodBatchAnalyzer(null!, observer));
        Assert.Throws<ArgumentNullException>(() => new PipelinedReachableMethodBatchAnalyzer(pool, null!));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodPrefetcher(null!));
        Assert.Throws<ArgumentNullException>(() => pool.Submit(null!));
        Assert.Throws<ArgumentNullException>(() => new DeferredReachableMethodPrefetcher().Prefetch(null!));
    }

    [Fact]
    public void BatchRejectsADefaultRequestBatchAndAcceptsAnEmptyOne()
    {
        using var session = CreateSession([new DelegatingAnalyzer(CreateAnalysis)], new DelegatingAnalyzer(CreateAnalysis));

        Assert.Throws<ArgumentException>(() => session.Batches.Analyze(default).ToArray());
        Assert.Empty(session.Batches.Analyze([]));
    }

    [Fact]
    public async Task PoolUsesIsolatedWorkersAndPublishesResultsInRequestOrder()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var analyzers = ImmutableArray.Create<IReachableMethodAnalyzer>(
            CreateConcurrentAnalyzer(),
            CreateConcurrentAnalyzer());
        using var session = CreateSession(analyzers, new DelegatingAnalyzer(CreateAnalysis));
        var requests = Enumerable.Range(0, 8)
            .Select(_ => new ReachableMethodRequest(CreateMethod()))
            .ToImmutableArray();
        foreach (var request in requests)
        {
            session.Prefetcher.Prefetch(request);
        }

        // Both workers hold a request until each has one, so they run at once.
        Assert.True(entered.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        release.Set();
        var results = await Task.Run(() => session.Batches.Analyze(requests).ToArray());

        Assert.Equal(requests.Select(request => request.Method), results.Select(result => result.Method));
        Assert.All(analyzers.Cast<DelegatingAnalyzer>(),
            worker => Assert.Equal(1, worker.MaximumConcurrentCalls));

        DelegatingAnalyzer CreateConcurrentAnalyzer()
        {
            var firstCall = 1;
            return new DelegatingAnalyzer(request =>
            {
                if (Interlocked.Exchange(ref firstCall, 0) == 1)
                {
                    entered.Signal();
                    release.Wait();
                }

                return CreateAnalysis(request);
            });
        }
    }

    [Fact]
    public void APrefetchedRequestIsAnalyzedOnceAndTakenByItsBatch()
    {
        var calls = 0;
        using var analyzed = new ManualResetEventSlim();
        var worker = new DelegatingAnalyzer(request =>
        {
            Interlocked.Increment(ref calls);
            analyzed.Set();
            return CreateAnalysis(request);
        });
        var observer = new RecordingReachableMethodBatchObserver();
        using var session = CreateSession([worker], new DelegatingAnalyzer(_ => throw new InvalidOperationException("closure")), observer);
        var request = new ReachableMethodRequest(CreateMethod());

        session.Prefetcher.Prefetch(request);
        session.Prefetcher.Prefetch(request);
        Assert.True(analyzed.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        var result = Assert.Single(session.Batches.Analyze([request]));

        Assert.Same(request.Method, result.Method);
        Assert.Equal(1, Volatile.Read(ref calls));
        var observation = Assert.Single(observer.Observations);
        Assert.Equal((1, 1), (observation.RequestCount, observation.WorkerCount));
        Assert.True(observation.WorkerBusy >= TimeSpan.Zero && observation.OrderedWait >= TimeSpan.Zero);
    }

    [Fact]
    public void TheClosureAnalyzesARequestNoWorkerHasStarted()
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocking = new ReachableMethodRequest(CreateMethod());
        var worker = new DelegatingAnalyzer(request =>
        {
            if (ReferenceEquals(request, blocking))
            {
                held.Set();
                release.Wait();
            }
            return CreateAnalysis(request);
        });
        var closureThread = 0;
        var closure = new DelegatingAnalyzer(request =>
        {
            closureThread = Environment.CurrentManagedThreadId;
            return CreateAnalysis(request);
        });
        using var session = CreateSession([worker], closure);
        session.Prefetcher.Prefetch(blocking);
        Assert.True(held.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        var waiting = new ReachableMethodRequest(CreateMethod());

        // The only worker is busy, so the closure takes the queued request itself.
        var result = Assert.Single(session.Batches.Analyze([waiting]));
        release.Set();

        Assert.Same(waiting.Method, result.Method);
        Assert.Equal(Environment.CurrentManagedThreadId, closureThread);
        Assert.Same(blocking.Method, Assert.Single(session.Batches.Analyze([blocking])).Method);
    }

    [Fact]
    public void BatchesPreserveTheEarliestFailureAndRejectAMissingResult()
    {
        var firstFailure = new InvalidOperationException("first");
        var secondFailure = new InvalidOperationException("second");
        var firstMethod = CreateMethod();
        var missingMethod = CreateMethod();
        ReachableMethodAnalysis Analyze(ReachableMethodRequest request) =>
            ReferenceEquals(request.Method, missingMethod) ? null!
            : throw (ReferenceEquals(request.Method, firstMethod) ? firstFailure : secondFailure);
        using var session = CreateSession(
            [new DelegatingAnalyzer(Analyze), new DelegatingAnalyzer(Analyze)],
            new DelegatingAnalyzer(Analyze));

        Assert.Same(firstFailure, Record.Exception(() => session.Batches.Analyze([
            new ReachableMethodRequest(firstMethod),
            new ReachableMethodRequest(CreateMethod()),
        ]).ToArray()));
        Assert.Throws<InvalidOperationException>(() =>
            session.Batches.Analyze([new ReachableMethodRequest(missingMethod)]).ToArray());
    }

    [Fact]
    public async Task EndingASessionCancelsQueuedRequestsAndWaitsForRunningOnes()
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var worker = new DelegatingAnalyzer(request =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                held.Set();
                release.Wait();
            }
            return CreateAnalysis(request);
        });
        var session = CreateSession([worker], new DelegatingAnalyzer(CreateAnalysis));
        session.Prefetcher.Prefetch(new ReachableMethodRequest(CreateMethod()));
        Assert.True(held.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        foreach (var _ in Enumerable.Range(0, 5))
        {
            session.Prefetcher.Prefetch(new ReachableMethodRequest(CreateMethod()));
        }

        var cancellation = TestContext.Current.CancellationToken;
        var ending = Task.Run(session.Dispose, cancellation);
        Assert.NotSame(ending, await Task.WhenAny(ending, Task.Delay(100, cancellation)));
        release.Set();
        await ending.WaitAsync(TimeSpan.FromMinutes(1), cancellation);

        Assert.Equal(1, Volatile.Read(ref calls));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            session.Prefetcher.Prefetch(new ReachableMethodRequest(CreateMethod())));
    }

    [Fact]
    public void WorkersSeeTheClosuresExecutionContext()
    {
        var context = new AsyncLocal<string?> { Value = "compilation" };
        var seen = new System.Collections.Concurrent.ConcurrentBag<string?>();
        using var analyzed = new CountdownEvent(4);
        var worker = new DelegatingAnalyzer(request =>
        {
            seen.Add(context.Value);
            analyzed.Signal();
            return CreateAnalysis(request);
        });
        using var session = CreateSession([worker, worker], worker);
        var requests = Enumerable.Range(0, 4).Select(_ => new ReachableMethodRequest(CreateMethod())).ToArray();

        foreach (var request in requests)
        {
            session.Prefetcher.Prefetch(request);
        }
        Assert.True(analyzed.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

        Assert.All(seen, value => Assert.Equal("compilation", value));
        Assert.Equal(4, session.Batches.Analyze([.. requests]).Count());
    }

    [Fact]
    public void SynchronousSessionsDeferPrefetchToTheirBatch()
    {
        var calls = 0;
        var factory = new SynchronousReachableMethodAnalysisSessionFactory(
            new DelegatingAnalyzer(request =>
            {
                calls++;
                return CreateAnalysis(request);
            }),
            new RecordingReachableMethodBatchObserver());
        using var session = factory.Begin();
        var request = new ReachableMethodRequest(CreateMethod());

        session.Prefetcher.Prefetch(request);
        Assert.Equal(0, calls);
        Assert.Same(request.Method, Assert.Single(session.Batches.Analyze([request])).Method);
        Assert.Equal(1, calls);
    }

    private static ReachableMethodAnalysisSession CreateSession(
        ImmutableArray<IReachableMethodAnalyzer> workers,
        IReachableMethodAnalyzer closure,
        RecordingReachableMethodBatchObserver? observer = null) =>
        new PooledReachableMethodAnalysisSessionFactory(
            workers, closure, observer ?? new RecordingReachableMethodBatchObserver()).Begin();

    private static ReachableMethodAnalysis CreateAnalysis(
        ReachableMethodRequest request) =>
        new(request.Method, null!, [], [], null!);

    private static MethodInstanceModel CreateMethod() =>
        (MethodInstanceModel)RuntimeHelpers.GetUninitializedObject(
            typeof(MethodInstanceModel));

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(
                ref maximum,
                candidate,
                current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private sealed class DelegatingAnalyzer(
        Func<ReachableMethodRequest, ReachableMethodAnalysis> analyze) :
        IReachableMethodAnalyzer
    {
        private int _activeCalls;
        private int _maximumConcurrentCalls;

        public int MaximumConcurrentCalls => _maximumConcurrentCalls;

        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximum(ref _maximumConcurrentCalls, active);
            try
            {
                return analyze(request);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }
}
