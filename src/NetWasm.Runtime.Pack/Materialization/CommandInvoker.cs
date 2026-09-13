using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class CommandInvoker : ICommandInvoker
{
    public void Invoke(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(command.LogPath))!);
        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            File.WriteAllText(command.LogPath, standardOutput.Result + standardError.Result);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"A NetWasm runtime tool failed; see '{Path.GetFullPath(command.LogPath)}'.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("A NetWasm runtime tool could not be executed.", exception);
        }
    }
}
