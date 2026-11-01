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
    public void ConstructorRejectsAnEmptyWorkerSet()
    {
        Assert.Throws<ArgumentException>(() =>
            new ReachableMethodBatchAnalyzer([], new RecordingReachableMethodBatchObserver()));
    }

    [Fact]
    public void ConstructorRejectsAMissingObserver()
    {
        var workers = ImmutableArray.Create<IReachableMethodAnalyzer>(
            new DelegatingAnalyzer(CreateAnalysis));

        Assert.Throws<ArgumentNullException>(
            () => new ReachableMethodBatchAnalyzer(workers, null!));
    }

    [Fact]
    public void AnalyzeRejectsADefaultRequestBatch()
    {
        var analyzer = new ReachableMethodBatchAnalyzer([
            new DelegatingAnalyzer(CreateAnalysis)
        ],
            new RecordingReachableMethodBatchObserver()
        );

        Assert.Throws<ArgumentException>(() =>
            analyzer.Analyze(default).ToArray());
    }

    [Fact]
    public void AnalyzeReturnsAnEmptyResultForAnEmptyBatch()
    {
        var analyzer = new ReachableMethodBatchAnalyzer([
            new DelegatingAnalyzer(CreateAnalysis)
        ],
            new RecordingReachableMethodBatchObserver()
        );

        Assert.Empty(analyzer.Analyze([]));
    }

    [Fact]
    public async Task AnalyzeUsesIsolatedWorkersAndPublishesResultsInRequestOrder()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var analyzers = ImmutableArray.Create<IReachableMethodAnalyzer>(
            CreateConcurrentAnalyzer(),
            CreateConcurrentAnalyzer());
        var analyzer = new ReachableMethodBatchAnalyzer(analyzers, new RecordingReachableMethodBatchObserver());
        var requests = Enumerable.Range(0, 8)
            .Select(_ => new ReachableMethodRequest(CreateMethod()))
            .ToImmutableArray();

        var analysisTask = Task.Run(() => analyzer.Analyze(requests).ToArray());

        Assert.True(entered.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        release.Set();
        var results = await analysisTask;

        Assert.Equal(requests.Length, results.Length);
        for (var index = 0; index < requests.Length; index++)
        {
            Assert.Same(requests[index].Method, results[index].Method);
        }

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
    public void AnalyzePreservesTheEarliestRequestFailure()
    {
        var firstFailure = new InvalidOperationException("first");
        var secondFailure = new InvalidOperationException("second");
        var firstMethod = CreateMethod();
        var secondMethod = CreateMethod();
        var analyzer = new ReachableMethodBatchAnalyzer([
            new DelegatingAnalyzer(request =>
                throw (ReferenceEquals(request.Method, firstMethod)
                    ? firstFailure
                    : secondFailure)),
            new DelegatingAnalyzer(request =>
                throw (ReferenceEquals(request.Method, firstMethod)
                    ? firstFailure
                    : secondFailure))
        ],
            new RecordingReachableMethodBatchObserver()
        );

        var failure = Record.Exception(() => analyzer.Analyze([
            new ReachableMethodRequest(firstMethod),
            new ReachableMethodRequest(secondMethod)
        ]).ToArray());

        Assert.Same(firstFailure, failure);
    }

    [Fact]
    public void AnalyzeRejectsAMissingWorkerResult()
    {
        var analyzer = new ReachableMethodBatchAnalyzer([
            new DelegatingAnalyzer(_ => null!)
        ],
            new RecordingReachableMethodBatchObserver()
        );

        Assert.Throws<InvalidOperationException>(() =>
            analyzer.Analyze([
                new ReachableMethodRequest(CreateMethod())
            ]).ToArray());
    }

    [Fact]
    public void AnalyzeReportsAggregateBatchMetrics()
    {
        var observer = new RecordingReachableMethodBatchObserver();
        var analyzer = new ReachableMethodBatchAnalyzer(
            ImmutableArray.Create<IReachableMethodAnalyzer>(
                new DelegatingAnalyzer(CreateAnalysis)),
            observer);
        var requests = ImmutableArray.Create(
            new ReachableMethodRequest(CreateMethod()));

        _ = analyzer.Analyze(requests).ToArray();

        var observation = Assert.Single(observer.Observations);
        Assert.Equal(1, observation.RequestCount);
        Assert.Equal(1, observation.WorkerCount);
        Assert.True(observation.Elapsed >= TimeSpan.Zero);
        Assert.True(observation.WorkerBusy >= TimeSpan.Zero);
        Assert.True(observation.OrderedWait >= TimeSpan.Zero);
        Assert.Equal(0, observation.PeakBufferedResults);
    }

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
