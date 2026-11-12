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
    public void WaitsForOutputThatArrivesSecondsAfterTheExit()
    {
        // A descendant holds the pipes for three seconds after the process exits, as a stalled
        // reader on a loaded host does; the output is complete only once they close.
        var result = Run("printf early; (sleep 3; printf late) &", TimeSpan.FromSeconds(30));

        Assert.True(result.Succeeded);
        Assert.Equal(QualifiedProcessCompletion.Exited, result.Completion);
        Assert.Equal("earlylate", result.StandardOutput);
    }

    [Fact]
    public void ReportsPipesStillOpenAfterTheTimeout()
    {
        var result = Run("printf early; (sleep 5) &", TimeSpan.FromMilliseconds(500));

        Assert.False(result.Succeeded);
        Assert.Equal(QualifiedProcessCompletion.LaunchFailed, result.Completion);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("process output pipes did not close within 2000 ms", result.CleanupFailure);
        Assert.IsType<TimeoutException>(result.LaunchException);
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
        Assert.True(Stops(childId));
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
        Assert.True(Stops(childId));
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

    // The runner returns once the shell it started exits, but the kill reaches the
    // shell's child asynchronously, and the child then stays listed, as a zombie,
    // until its new parent reaps it. Checking it at once raced both (0.11.3 preview,
    // full-suite shard 14), so the child is given a bounded time to stop, and a
    // zombie has stopped.
    private static bool Stops(int processId) =>
        SpinWait.SpinUntil(() => !IsRunning(processId), TimeSpan.FromSeconds(10));

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited && State(processId) is not (null or 'Z');
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }
    }

    // The process's state letter: Linux gives it after the parenthesized command in
    // /proc/<pid>/stat, macOS through ps; null once the process is gone.
    private static char? State(int processId)
    {
        var id = processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stat = $"/proc/{id}/stat";
        if (Directory.Exists("/proc/self"))
        {
            var text = File.ReadAllText(stat);
            return text[text.LastIndexOf(')') + 2];
        }
        using var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ps", ["-o", "stat=", "-p", id])
        {
            RedirectStandardOutput = true,
        })!;
        var state = ps.StandardOutput.ReadToEnd().Trim();
        ps.WaitForExit();
        return state.Length == 0 ? null : state[0];
    }
}
