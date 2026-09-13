using System.Diagnostics;
using System.Text.RegularExpressions;
using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Hosting.Build.Environment;

public sealed partial class ProcessHostToolCompatibilityProbe : IHostToolCompatibilityProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public HostToolCompatibilityObservation Observe(ResolvedHostExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.ToolId);
        if (!Path.IsPathFullyQualified(executable.AbsolutePath))
        {
            throw new ArgumentException(
                "The host tool executable path must be absolute.",
                nameof(executable));
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable.AbsolutePath),
        };
        try
        {
            if (!process.Start())
            {
                throw Invalid(executable.ToolId, "could not be started");
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
                throw Invalid(executable.ToolId, "did not report a version in time", exception);
            }

            var output = string.Concat(
                standardOutput.GetAwaiter().GetResult(),
                "\n",
                standardError.GetAwaiter().GetResult());
            if (process.ExitCode != 0)
            {
                throw Invalid(executable.ToolId, "returned a nonzero version-probe exit code");
            }

            var match = VersionPattern().Match(output);
            if (!match.Success || !Version.TryParse(match.Value, out var version))
            {
                throw Invalid(executable.ToolId, "returned an unrecognized version");
            }

            return new(executable.ToolId, version, []);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Invalid(executable.ToolId, "could not be probed", exception);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--version");
        return startInfo;
    }

    private static InvalidOperationException Invalid(
        string toolId,
        string reason,
        Exception? inner = null) => new(
            $"Host tool '{toolId}' {reason}.",
            inner);

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

    [GeneratedRegex(@"(?<![0-9])[0-9]+\.[0-9]+(?:\.[0-9]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
