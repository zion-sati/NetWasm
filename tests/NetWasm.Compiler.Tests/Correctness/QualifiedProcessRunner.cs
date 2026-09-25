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

            var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var standardError = ReadStandardErrorAsync(
                process.StandardError,
                request.Progress);
            using var timeout = new CancellationTokenSource(request.Timeout);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                cancellationToken);
            try
            {
                process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
                when (cancellation.IsCancellationRequested)
            {
                var cleanup = Terminate(process);
                var streams = ReadStreams(standardOutput, standardError);
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

            var completedStreams = ReadStreams(standardOutput, standardError);
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

    private static async Task<string> ReadStandardErrorAsync(
        StreamReader reader,
        Action<int, int>? progress)
    {
        const string prefix = "NETWASM_PROGRESS ";
        var lines = new List<string>();
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
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
        Task<string> standardError)
    {
        var streams = Task.WhenAll(standardOutput, standardError);
        if (!streams.Wait(ForcedTerminationTimeout))
        {
            return new(
                standardOutput.IsCompletedSuccessfully ? standardOutput.Result : string.Empty,
                standardError.IsCompletedSuccessfully ? standardError.Result : string.Empty,
                $"process output pipes did not close within " +
                $"{ForcedTerminationTimeout.TotalMilliseconds} ms");
        }
        return new(standardOutput.Result, standardError.Result, null);
    }

    private readonly record struct ProcessCleanupResult(
        QualifiedProcessTermination Termination,
        string? Failure);

    private readonly record struct ProcessStreamResult(
        string StandardOutput,
        string StandardError,
        string? Failure);
}
