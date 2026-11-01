using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class QualifiedProcessRunnerTests
{
    private readonly QualifiedProcessRunner _runner = new();

    [Fact]
    public void CapturesSuccessfulOutputAndExitCode()
    {
        var result = Run("printf 'out'; printf 'err' >&2; exit 0");

        Assert.True(result.Succeeded);
        Assert.Equal(QualifiedProcessCompletion.Exited, result.Completion);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("out", result.StandardOutput);
        Assert.Equal("err", result.StandardError);
    }

    [Fact]
    public void CapturesNonZeroExitWithoutMisclassifyingItAsLaunchFailure()
    {
        var result = Run("exit 17");

        Assert.False(result.Succeeded);
        Assert.Equal(QualifiedProcessCompletion.Exited, result.Completion);
        Assert.Equal(17, result.ExitCode);
    }

    [Fact]
    public void ConsumesProgressProtocolWithoutPollutingDiagnostics()
    {
        var reports = new List<(int Completed, int Total)>();
        var result = _runner.Run(new(
            "/bin/sh",
            [
                "-c",
                "printf 'NETWASM_PROGRESS 3/10\\nkept' >&2",
            ],
            TimeSpan.FromSeconds(5))
        {
            Progress = (completed, total) => reports.Add((completed, total)),
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal([(3, 10)], reports);
        Assert.Equal("kept", result.StandardError);
    }

    [Fact]
    public void TimesOutAndKillsTheProcessTree()
    {
        var directory = CorrectnessTestAssets.CreateDirectory();
        var childPath = Path.Combine(directory, "child.pid");
        var timeout = TimeSpan.FromMilliseconds(200);
        var result = Run(
            $"trap '' TERM; sleep 30 & child=$!; " +
            $"printf '%s' \"$child\" > '{childPath}'; wait",
            timeout);

        Assert.False(result.Succeeded);
        Assert.Equal(QualifiedProcessCompletion.TimedOut, result.Completion);
        Assert.Equal(QualifiedProcessTermination.Forced, result.Termination);
        Assert.Null(result.CleanupFailure);
        Assert.Null(result.ExitCode);
        // Scheduling delays in the sharded suite are not a cleanup failure.
        // The termination result and child-state assertion prove cleanup.
        Assert.True(result.Elapsed >= timeout);
        var childId = int.Parse(File.ReadAllText(childPath),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.False(IsRunning(childId));
    }

    [Fact]
    public async Task CancellationKillsTheProcessTreeAndRemainsCancellation()
    {
        var directory = CorrectnessTestAssets.CreateDirectory();
        var childPath = Path.Combine(directory, "cancelled-child.pid");
        using var cancellation = new CancellationTokenSource();
        var run = Task.Run(() => _runner.Run(new(
            "/bin/sh",
            [
                "-c",
                $"trap '' TERM; sleep 30 & child=$!; " +
                $"printf '%s' \"$child\" > '{childPath}'; wait",
            ],
            TimeSpan.FromSeconds(30)), cancellation.Token));

        Assert.True(SpinWait.SpinUntil(
            () => File.Exists(childPath),
            TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var childId = int.Parse(File.ReadAllText(childPath),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.False(IsRunning(childId));
    }

    [Fact]
    public void ClassifiesLaunchFailure()
    {
        var result = _runner.Run(new(
            "/definitely/not/a/netwasm/executable",
            ImmutableArray<string>.Empty,
            TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken);

        Assert.Equal(QualifiedProcessCompletion.LaunchFailed, result.Completion);
        Assert.NotNull(result.LaunchException);
    }

    [Fact]
    public void RejectsNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.Run(new(
            "/bin/sh", ["-c", "exit 0"], TimeSpan.Zero), TestContext.Current.CancellationToken));
    }

    private QualifiedProcessResult Run(
        string command,
        TimeSpan? timeout = null) => _runner.Run(new(
            "/bin/sh",
            ["-c", command],
            timeout ?? TimeSpan.FromSeconds(5)));

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
