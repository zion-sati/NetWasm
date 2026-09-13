using System.Collections.Immutable;
using System.Diagnostics;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record JavaScriptProcessRequest(
    string NodePath,
    string EntryPointPath,
    ImmutableArray<string> Arguments,
    string Operation);

public interface IJavaScriptProcessRunner
{
    void Run(JavaScriptProcessRequest request);
}

public sealed class JavaScriptProcessRunner : IJavaScriptProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public void Run(JavaScriptProcessRequest request)
    {
        Validate(request);
        using var process = new Process { StartInfo = CreateStartInfo(request) };
        try
        {
            if (!process.Start())
            {
                throw Failure(request.Operation, "could not start");
            }
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(Timeout);
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                TryKill(process);
                throw Failure(request.Operation, "timed out", exception);
            }

            _ = standardOutput.GetAwaiter().GetResult();
            _ = standardError.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw Failure(request.Operation, "failed");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure(request.Operation, "could not execute", exception);
        }
    }

    private static ProcessStartInfo CreateStartInfo(JavaScriptProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.NodePath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(request.EntryPointPath);
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var name in startInfo.Environment.Keys
                     .Where(name => name.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("NPM_CONFIG_", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            startInfo.Environment.Remove(name);
        }
        return startInfo;
    }

    private static void Validate(JavaScriptProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var path in new[] { request.NodePath, request.EntryPointPath })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("JavaScript process paths must be absolute.", nameof(request));
            }
        }
        if (request.Arguments.IsDefault || request.Arguments.Any(argument => argument is null))
        {
            throw new ArgumentException("JavaScript process arguments must be explicit.", nameof(request));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation);
    }

    private static InvalidOperationException Failure(
        string operation,
        string reason,
        Exception? inner = null) =>
        new($"The NetWasm {operation} {reason}.", inner);

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
