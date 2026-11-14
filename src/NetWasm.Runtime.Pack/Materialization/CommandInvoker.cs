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
            var output = standardOutput.Result + standardError.Result;
            File.WriteAllText(command.LogPath, output);
            if (process.ExitCode != 0)
            {
                // The tool's last lines go in the message too, since NWPACK001 is often all a build log keeps.
                throw new InvalidOperationException(
                    $"A NetWasm runtime tool ('{Path.GetFileName(command.ExecutablePath)}') failed with exit code " +
                    $"{process.ExitCode}; see '{Path.GetFullPath(command.LogPath)}'.{Tail(output)}");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The cause goes in the message: NWPACK001 reports only the message (zion-sati/NetWasm#236).
            throw new InvalidOperationException(
                $"A NetWasm runtime tool could not be executed ('{command.ExecutablePath}'): {exception.Message}", exception);
        }
    }

    private const int TailLines = 20;

    private static string Tail(string output)
    {
        var lines = output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        return lines is [""] ? "" : " Its last output:\n" + string.Join('\n', lines[Math.Max(0, lines.Length - TailLines)..]);
    }
}
