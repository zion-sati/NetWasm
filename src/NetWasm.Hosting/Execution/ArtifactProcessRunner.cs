using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Hosting.Execution;

internal sealed class ArtifactProcessRunner : IArtifactProcessRunner
{
    public async ValueTask<int> RunAsync(
        ArtifactProcessStart start,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        ValidateOutput(standardOutput, nameof(standardOutput));
        ValidateOutput(standardError, nameof(standardError));
        if (ReferenceEquals(standardOutput, standardError))
        {
            throw new ArgumentException("NetWasm standard output and error streams must be distinct.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        var processStart = new ProcessStartInfo
        {
            FileName = start.FileName,
            WorkingDirectory = start.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in start.Arguments)
        {
            processStart.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = processStart };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The NetWasm artifact host process did not start.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("The NetWasm artifact host process could not be started.", exception);
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state => Terminate((Process)state!),
            process);
        // Cancellation terminates the process; its closed pipes must still be drained so
        // neither copy can be abandoned while it owns a caller-provided stream.
        var standardOutputCopy = process.StandardOutput.BaseStream.CopyToAsync(
            standardOutput,
            CancellationToken.None);
        var standardErrorCopy = process.StandardError.BaseStream.CopyToAsync(
            standardError,
            CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.WhenAll(standardOutputCopy, standardErrorCopy).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static void ValidateOutput(Stream stream, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(stream, parameterName);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("A NetWasm artifact output stream must be writable.", parameterName);
        }
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
