using System.Diagnostics;
using System.Globalization;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class QualifiedProcessRunner : IQualifiedProcessRunner
{
    private static readonly TimeSpan GracefulTerminationTimeout =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ForcedTerminationTimeout =
        TimeSpan.FromSeconds(2);

    public QualifiedProcessResult Run(
        QualifiedProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.FileName);
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var start = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };
        foreach (var argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var variable in request.EnvironmentVariables)
        {
            start.Environment[variable.Key] = variable.Value;
        }

        var stopwatch = Stopwatch.StartNew();
        Process? process = null;
        try
        {
            process = Process.Start(start);
            if (process is null)
            {
                throw new InvalidOperationException(
                    $"failed to start process '{request.FileName}'");
            }

            // Nothing here waits on the thread pool: the test host runs many processes at once and
            // blocks pool threads on them, so a read, exit or timeout that needed a pool thread to
            // complete could miss its bound for a process that had exited. Each pipe is read on a
            // thread of its own, and the exit is awaited on this one.
            var output = process.StandardOutput;
            var error = process.StandardError;
            var standardOutput = ReadOnOwnThread(output.ReadToEnd);
            var standardError = ReadOnOwnThread(() => ReadStandardError(error, request.Progress));
            if (!WaitForExit(process, request.Timeout, cancellationToken))
            {
                var cleanup = Terminate(process);
                var streams = ReadStreams(
                    standardOutput, standardError, ForcedTerminationTimeout, CancellationToken.None);
                stopwatch.Stop();
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return new(
                    QualifiedProcessCompletion.TimedOut,
                    null,
                    streams.StandardOutput,
                    streams.StandardError,
                    stopwatch.Elapsed)
                {
                    Termination = cleanup.Termination,
                    CleanupFailure = cleanup.Failure ?? streams.Failure,
                };
            }

            // The pipes close when every process holding them has exited, which a loaded host may
            // see seconds after the exit itself; only the request's own timeout bounds that.
            var completedStreams = ReadStreams(
                standardOutput,
                standardError,
                Max(request.Timeout - stopwatch.Elapsed, ForcedTerminationTimeout),
                cancellationToken);
            if (completedStreams.Failure is not null)
            {
                stopwatch.Stop();
                return new(
                    QualifiedProcessCompletion.LaunchFailed,
                    process.ExitCode,
                    completedStreams.StandardOutput,
                    completedStreams.StandardError,
                    stopwatch.Elapsed)
                {
                    LaunchException = new TimeoutException(completedStreams.Failure),
                    CleanupFailure = completedStreams.Failure,
                };
            }
            stopwatch.Stop();
            return new(
                QualifiedProcessCompletion.Exited,
                process.ExitCode,
                completedStreams.StandardOutput,
                completedStreams.StandardError,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new(
                QualifiedProcessCompletion.LaunchFailed,
                null,
                string.Empty,
                string.Empty,
                stopwatch.Elapsed)
            {
                LaunchException = exception,
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromMilliseconds(50);

    // Whether the process exited before the timeout or the cancellation.
    private static bool WaitForExit(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (!process.WaitForExit(ExitPollInterval))
        {
            if (cancellationToken.IsCancellationRequested || waited.Elapsed >= timeout)
                return false;
        }
        return true;
    }

    private static Task<string> ReadOnOwnThread(Func<string> read) => Task.Factory.StartNew(
        read, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static string ReadStandardError(
        StreamReader reader,
        Action<int, int>? progress)
    {
        const string prefix = "NETWASM_PROGRESS ";
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal) &&
                TryParseProgress(line.AsSpan(prefix.Length), out var completed, out var total))
            {
                progress?.Invoke(completed, total);
                continue;
            }
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static bool TryParseProgress(
        ReadOnlySpan<char> value,
        out int completed,
        out int total)
    {
        completed = 0;
        total = 0;
        var separator = value.IndexOf('/');
        return separator > 0 &&
            int.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture,
                out completed) &&
            int.TryParse(value[(separator + 1)..], NumberStyles.None,
                CultureInfo.InvariantCulture, out total) &&
            completed >= 0 && completed <= total && total > 0;
    }

    private static ProcessCleanupResult Terminate(Process process)
    {
        if (process.HasExited)
        {
            return new(QualifiedProcessTermination.Graceful, null);
        }

        SendTerminate(process.Id);
        if (process.WaitForExit((int)GracefulTerminationTimeout.TotalMilliseconds))
        {
            return new(QualifiedProcessTermination.Graceful, null);
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return new(QualifiedProcessTermination.Graceful, null);
        }
        if (process.WaitForExit((int)ForcedTerminationTimeout.TotalMilliseconds))
        {
            return new(QualifiedProcessTermination.Forced, null);
        }
        return new(
            QualifiedProcessTermination.Failed,
            $"process {process.Id} did not exit within " +
            $"{ForcedTerminationTimeout.TotalMilliseconds} ms after forced termination");
    }

    private static void SendTerminate(int processId)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var signal = Process.Start(new ProcessStartInfo("/bin/kill")
        {
            ArgumentList = { "-TERM", processId.ToString(
                System.Globalization.CultureInfo.InvariantCulture) },
        });
        if (signal is not null &&
            !signal.WaitForExit((int)GracefulTerminationTimeout.TotalMilliseconds))
        {
            signal.Kill();
            _ = signal.WaitForExit((int)ForcedTerminationTimeout.TotalMilliseconds);
        }
    }

    private static ProcessStreamResult ReadStreams(
        Task<string> standardOutput,
        Task<string> standardError,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!Task.WaitAll([standardOutput, standardError], (int)timeout.TotalMilliseconds, cancellationToken))
        {
            return new(
                standardOutput.IsCompletedSuccessfully ? standardOutput.Result : string.Empty,
                standardError.IsCompletedSuccessfully ? standardError.Result : string.Empty,
                $"process output pipes did not close within " +
                $"{timeout.TotalMilliseconds:F0} ms");
        }
        return new(standardOutput.Result, standardError.Result, null);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private readonly record struct ProcessCleanupResult(
        QualifiedProcessTermination Termination,
        string? Failure);

    private readonly record struct ProcessStreamResult(
        string StandardOutput,
        string StandardError,
        string? Failure);
}
