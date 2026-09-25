using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RandomCilCampaignOptions(
    int WorkerCount,
    TimeSpan HeartbeatInterval)
{
    public static RandomCilCampaignOptions CreateDefault()
    {
        var configured = Environment.GetEnvironmentVariable("NETWASM_RIT_WORKERS");
        return Create(configured);
    }

    internal static RandomCilCampaignOptions Create(string? configured)
    {
        if (configured is null)
        {
            return new(Environment.ProcessorCount, TimeSpan.FromSeconds(15));
        }
        if (!int.TryParse(configured, out var workerCount) || workerCount <= 0)
        {
            throw new InvalidOperationException(
                "NETWASM_RIT_WORKERS must be a positive integer");
        }
        return new(workerCount, TimeSpan.FromSeconds(15));
    }
}

internal sealed record RandomCilCampaignCase(
    string Id,
    Action<CancellationToken> Execute,
    Action<CancellationToken> ExecuteWithDiagnostics);

internal sealed record RandomCilCampaignResult(
    string Id,
    TimeSpan Duration,
    Exception? Failure)
{
    public Exception? DiagnosticReplayFailure { get; init; }
}

internal interface IRandomCilCampaignScheduler
{
    Task<ImmutableArray<RandomCilCampaignResult>> RunAsync(
        ImmutableArray<RandomCilCampaignCase> cases,
        CancellationToken cancellationToken = default);
}

internal interface IRandomCilCampaignProgressReporter
{
    void CampaignStarted(int total, int workers);

    void CaseStarted(
        string id,
        int active,
        int completed,
        int total,
        TimeSpan campaignElapsed);

    void CaseFinished(
        RandomCilCampaignResult result,
        int active,
        int completed,
        int total,
        TimeSpan campaignElapsed);

    void Heartbeat(
        ImmutableArray<string> activeCases,
        int completed,
        int total,
        TimeSpan campaignElapsed);

    void DiagnosticReplayStarted(string id, TimeSpan campaignElapsed);

    void DiagnosticReplayFinished(
        string id,
        TimeSpan duration,
        Exception? failure,
        TimeSpan campaignElapsed);

    void FlushPending()
    {
    }
}

internal sealed class ConsoleRandomCilCampaignProgressReporter :
    IRandomCilCampaignProgressReporter
{
    private readonly RitProgressCounterStore _counters;
    private readonly RitProgressSnapshotWriter _writer;

    public ConsoleRandomCilCampaignProgressReporter(
        RitProgressCounterStore counters,
        RitProgressSnapshotWriter writer)
    {
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    internal ConsoleRandomCilCampaignProgressReporter(TextWriter output)
        : this(new RitProgressCounterStore(), output)
    {
    }

    private ConsoleRandomCilCampaignProgressReporter(
        RitProgressCounterStore counters,
        TextWriter output)
        : this(counters, new RitProgressSnapshotWriter(counters, output))
    {
    }

    public void CampaignStarted(int total, int workers) => WriteAndFlush(
        $"RIT start total={total} workers={workers}");

    public void CaseStarted(
        string id,
        int active,
        int completed,
        int total,
        TimeSpan campaignElapsed) => Write(
        $"RIT case-start id={id} active={active} completed={completed}/{total} " +
        $"elapsed={Format(campaignElapsed)}");

    public void CaseFinished(
        RandomCilCampaignResult result,
        int active,
        int completed,
        int total,
        TimeSpan campaignElapsed) => Write(
        $"RIT case-finish id={result.Id} status=" +
        $"{(result.Failure is null ? "PASS" : "FAIL")} " +
        $"detail={Concise(result.Failure)} " +
        $"case={Format(result.Duration)} active={active} " +
        $"completed={completed}/{total} elapsed={Format(campaignElapsed)}");

    public void Heartbeat(
        ImmutableArray<string> activeCases,
        int completed,
        int total,
        TimeSpan campaignElapsed) => WriteAndFlush(
        $"RIT heartbeat active={activeCases.Length} completed={completed}/{total} " +
        $"elapsed={Format(campaignElapsed)} cases=[{string.Join(',', activeCases)}]");

    public void DiagnosticReplayStarted(string id, TimeSpan campaignElapsed) => WriteAndFlush(
        $"RIT diagnostic-replay-start id={id} elapsed={Format(campaignElapsed)}");

    public void DiagnosticReplayFinished(
        string id,
        TimeSpan duration,
        Exception? failure,
        TimeSpan campaignElapsed) => WriteAndFlush(
        $"RIT diagnostic-replay-finish id={id} status=" +
        $"{(failure is null ? "UNEXPECTED-PASS" : "CAPTURED")} " +
        $"detail={Concise(failure)} " +
        $"case={Format(duration)} elapsed={Format(campaignElapsed)}");

    private void Write(string message)
    {
        _counters.PublishEvent(message);
    }

    private void WriteAndFlush(string message)
    {
        Write(message);
        FlushPending();
    }

    public void FlushPending() => _writer.WritePending();

    private static string Format(TimeSpan duration) =>
        duration.ToString(@"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);

    private static string Concise(Exception? failure)
    {
        if (failure is null)
        {
            return "none";
        }

        var firstLine = FirstLine(failure.Message);
        return firstLine.Length <= 240 ? firstLine : firstLine[..240];
    }

    private static string FirstLine(string value)
    {
        var lineEnd = value.IndexOfAny(['\r', '\n']);
        return lineEnd < 0 ? value : value[..lineEnd];
    }
}

internal sealed class RandomCilCampaignScheduler(
    RandomCilCampaignOptions options,
    IRandomCilCampaignProgressReporter progress) :
    IRandomCilCampaignScheduler
{
    public async Task<ImmutableArray<RandomCilCampaignResult>> RunAsync(
        ImmutableArray<RandomCilCampaignCase> cases,
        CancellationToken cancellationToken = default)
    {
        if (cases.IsDefaultOrEmpty)
        {
            return [];
        }
        if (cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() !=
            cases.Length)
        {
            throw new ArgumentException("RIT case IDs must be unique", nameof(cases));
        }

        var campaignStarted = TimeProvider.System.GetTimestamp();
        var results = new ConcurrentBag<RandomCilCampaignResult>();
        var active = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var completed = 0;
        RandomCilCampaignCase? failedCase = null;
        progress.CampaignStarted(cases.Length, options.WorkerCount);
        using var campaignCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatCancellation = new CancellationTokenSource();
        var heartbeat = ReportHeartbeatsAsync(
            active,
            () => Volatile.Read(ref completed),
            cases.Length,
            campaignStarted,
            heartbeatCancellation.Token);
        try
        {
            try
            {
                await Parallel.ForEachAsync(
                    cases,
                    new ParallelOptions
                    {
                        CancellationToken = campaignCancellation.Token,
                        MaxDegreeOfParallelism = options.WorkerCount,
                    },
                    async (testCase, token) =>
                    {
                        var started = TimeProvider.System.GetTimestamp();
                        active.TryAdd(testCase.Id, 0);
                        progress.CaseStarted(
                            testCase.Id,
                            active.Count,
                            Volatile.Read(ref completed),
                            cases.Length,
                            TimeProvider.System.GetElapsedTime(campaignStarted));
                        Exception? failure = null;
                        try
                        {
                            await Task.Run(() => testCase.Execute(token), token);
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                            if (Interlocked.CompareExchange(
                                    ref failedCase,
                                    testCase,
                                    null) is null)
                            {
                                campaignCancellation.Cancel();
                            }
                        }
                        var result = new RandomCilCampaignResult(
                            testCase.Id,
                            TimeProvider.System.GetElapsedTime(started),
                            failure);
                        results.Add(result);
                        active.TryRemove(testCase.Id, out _);
                        var completedCount = Interlocked.Increment(ref completed);
                        progress.CaseFinished(
                            result,
                            active.Count,
                            completedCount,
                            cases.Length,
                            TimeProvider.System.GetElapsedTime(campaignStarted));
                    });
            }
            catch (OperationCanceledException)
                when (failedCase is not null && !cancellationToken.IsCancellationRequested)
            {
            }
        }
        finally
        {
        }

        if (failedCase is not null)
        {
            var replayStarted = TimeProvider.System.GetTimestamp();
            progress.DiagnosticReplayStarted(
                failedCase.Id,
                TimeProvider.System.GetElapsedTime(campaignStarted));
            Exception? replayFailure = null;
            try
            {
                await Task.Run(
                    () => failedCase.ExecuteWithDiagnostics(cancellationToken),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                replayFailure = exception;
            }
            var replayDuration = TimeProvider.System.GetElapsedTime(replayStarted);
            progress.DiagnosticReplayFinished(
                failedCase.Id,
                replayDuration,
                replayFailure,
                TimeProvider.System.GetElapsedTime(campaignStarted));
            var original = results.Single(result => result.Id == failedCase.Id);
            results = new(results.Where(result => result.Id != failedCase.Id))
            {
                original with
                {
                    DiagnosticReplayFailure = replayFailure ??
                        new InvalidOperationException(
                            "RIT failure did not reproduce during diagnostic replay"),
                },
            };
        }

        heartbeatCancellation.Cancel();
        await heartbeat;
        progress.FlushPending();

        return [.. results.OrderBy(result => result.Id, StringComparer.Ordinal)];
    }

    private async Task ReportHeartbeatsAsync(
        ConcurrentDictionary<string, byte> active,
        Func<int> completed,
        int total,
        long campaignStarted,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                progress.Heartbeat(
                    [.. active.Keys.Order(StringComparer.Ordinal)],
                    completed(),
                    total,
                    TimeProvider.System.GetElapsedTime(campaignStarted));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
