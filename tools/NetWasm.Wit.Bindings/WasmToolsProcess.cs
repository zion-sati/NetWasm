using System.Diagnostics;

namespace NetWasm.Wit.Bindings;

internal interface IExternalCommandRunner
{
    ToolResult Run(string executable, IEnumerable<string> arguments);
}

internal sealed class SystemExternalCommandRunner : IExternalCommandRunner
{
    public ToolResult Run(string executable, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          System.ComponentModel.Win32Exception)
        {
            throw WitBindingException.Invalid(
                $"unable to start '{executable}': {exception.Message}");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);
        return new ToolResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
    }
}

internal sealed class WasmToolsProcess(IExternalCommandRunner commands) : IWasmTools
{
    private readonly IExternalCommandRunner _commands = commands ??
        throw new ArgumentNullException(nameof(commands));

    public ToolResult Run(params IEnumerable<string> arguments) =>
        _commands.Run("wasm-tools", arguments);
}
