using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class RandomCilCampaignSchedulerTests
{
    [Fact]
    public async Task UsesConfiguredWorkersWhileEachCaseExecutesOnce()
    {
        var active = 0;
        var maximumActive = 0;
        var executions = new int[8];
        using var release = new ManualResetEventSlim();
        var progress = new RecordingProgressReporter();
        var subject = new RandomCilCampaignScheduler(
            new(4, TimeSpan.FromMilliseconds(10)),
            progress);
        var cases = Enumerable.Range(0, executions.Length)
            .Select(index => new RandomCilCampaignCase(
                $"case-{index:D2}",
                _ =>
                {
                    Interlocked.Increment(ref executions[index]);
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                    Interlocked.Decrement(ref active);
                },
                _ => throw new InvalidOperationException("not expected")))
            .ToImmutableArray();

        var run = subject.RunAsync(cases, TestContext.Current.CancellationToken);
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref maximumActive) == 4,
            TimeSpan.FromSeconds(5)));
        release.Set();
        var results = await run;

        Assert.Equal(4, maximumActive);
        Assert.All(executions, count => Assert.Equal(1, count));
        Assert.All(results, result => Assert.Null(result.Failure));
        Assert.Equal(
            results.OrderBy(result => result.Id, StringComparer.Ordinal),
            results);
        Assert.Equal(executions.Length, progress.Started.Count);
        Assert.Equal(executions.Length, progress.Finished.Count);
    }

    [Fact]
    public async Task StopsSchedulingAfterFirstFailureAndRunsDiagnosticReplay()
    {
        var completed = 0;
        var subject = new RandomCilCampaignScheduler(
            new(1, TimeSpan.FromSeconds(1)),
            new RecordingProgressReporter());
        var results = await subject.RunAsync(
        [
            new(
                "failure",
                _ => throw new InvalidOperationException("expected"),
                _ => throw new InvalidOperationException("diagnostic expected")),
            new(
                "success",
                _ => Interlocked.Increment(ref completed),
                _ => throw new InvalidOperationException("not expected")),
        ], TestContext.Current.CancellationToken);

        Assert.Equal(0, completed);
        Assert.IsType<InvalidOperationException>(results[0].Failure);
        Assert.IsType<InvalidOperationException>(results[0].DiagnosticReplayFailure);
        Assert.Single(results);
    }

    [Fact]
    public async Task HeartbeatContinuesWhileFailedCampaignDrainsActiveCases()
    {
        using var activeCaseStarted = new ManualResetEventSlim();
        using var cancellationObserved = new ManualResetEventSlim();
        using var releaseActiveCase = new ManualResetEventSlim();
        var progress = new RecordingProgressReporter();
        var subject = new RandomCilCampaignScheduler(
            new(2, TimeSpan.FromMilliseconds(10)),
            progress);
        var run = subject.RunAsync(
        [
            new(
                "failure",
                _ =>
                {
                    Assert.True(activeCaseStarted.Wait(
                        TimeSpan.FromSeconds(5),
                        CancellationToken.None));
                    throw new InvalidOperationException("expected");
                },
                _ => throw new InvalidOperationException("diagnostic expected")),
            new(
                "active",
                token =>
                {
                    activeCaseStarted.Set();
                    Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
                    cancellationObserved.Set();
                    releaseActiveCase.Wait(CancellationToken.None);
                },
                _ => throw new InvalidOperationException("not expected")),
        ], TestContext.Current.CancellationToken);

        try
        {
            Assert.True(cancellationObserved.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var heartbeatCountAfterFailure = progress.HeartbeatCount;
            Assert.True(SpinWait.SpinUntil(
                () => progress.HeartbeatCount > heartbeatCountAfterFailure,
                TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseActiveCase.Set();
            await run;
        }

        var results = await run;
        Assert.Null(Assert.Single(results, result => result.Id == "active").Failure);
        Assert.IsType<InvalidOperationException>(
            Assert.Single(results, result => result.Id == "failure").Failure);
    }

    [Fact]
    public void DefaultsToEveryAvailableProcessorAndValidatesOverride()
    {
        Assert.Equal(
            Environment.ProcessorCount,
            RandomCilCampaignOptions.Create(null).WorkerCount);
        Assert.Equal(3, RandomCilCampaignOptions.Create("3").WorkerCount);
        Assert.Throws<InvalidOperationException>(() =>
            RandomCilCampaignOptions.Create("0"));
    }

    private sealed class RecordingProgressReporter :
        IRandomCilCampaignProgressReporter
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Started { get; } = [];

        public System.Collections.Concurrent.ConcurrentBag<string> Finished { get; } = [];

        public int HeartbeatCount => Volatile.Read(ref heartbeatCount);

        private int heartbeatCount;

        public void CampaignStarted(int total, int workers)
        {
        }

        public void CaseStarted(
            string id,
            int active,
            int completed,
            int total,
            TimeSpan campaignElapsed) => Started.Add(id);

        public void CaseFinished(
            RandomCilCampaignResult result,
            int active,
            int completed,
            int total,
            TimeSpan campaignElapsed) => Finished.Add(result.Id);

        public void Heartbeat(
            ImmutableArray<string> activeCases,
            int completed,
            int total,
            TimeSpan campaignElapsed)
        {
            Interlocked.Increment(ref heartbeatCount);
        }

        public void DiagnosticReplayStarted(string id, TimeSpan campaignElapsed)
        {
        }

        public void DiagnosticReplayFinished(
            string id,
            TimeSpan duration,
            Exception? failure,
            TimeSpan campaignElapsed)
        {
        }
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (candidate <= observed ||
                Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }
}
