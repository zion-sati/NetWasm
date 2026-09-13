using System.ComponentModel;
using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class TestHostProcessStarter : ITestHostProcessStarter
{
    private readonly ITestHostProcessTerminator _terminator;

    internal TestHostProcessStarter(ITestHostProcessTerminator terminator)
    {
        _terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
    }

    public ITestHostProcess Start(
        TestProcessStartInfo startInfo,
        Action<string> standardOutput,
        Action<string> standardError,
        Action<int, int> exited)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(exited);
        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.WorkingDirectory);

        var nativeStartInfo = new ProcessStartInfo
        {
            FileName = startInfo.FileName,
            Arguments = startInfo.Arguments ?? string.Empty,
            WorkingDirectory = startInfo.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (startInfo.EnvironmentVariables is not null)
        {
            foreach (var (name, value) in startInfo.EnvironmentVariables)
            {
                if (value is null)
                {
                    nativeStartInfo.Environment.Remove(name);
                }
                else
                {
                    nativeStartInfo.Environment[name] = value;
                }
            }
        }

        var process = new Process
        {
            StartInfo = nativeStartInfo,
        };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                standardOutput(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                standardError(eventArgs.Data);
            }
        };

        try
        {
            process.Start();
            var result = new TestHostProcess(process, exited, _terminator);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return result;
        }
        catch (Win32Exception exception)
        {
            process.Dispose();
            throw new InvalidOperationException(
                "The packaged NetWasm testhost could not be started.",
                exception);
        }
    }
}
