using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WasmToolsProcessTests
{
    [Fact]
    public void AdapterInvokesWasmToolsThroughItsCapability()
    {
        var commands = new RecordingCommandRunner(new ToolResult(7, "out", "error"));
#pragma warning disable CA1859 // Contract tests deliberately dispatch through the one-action interface.
        IWasmTools tools = new WasmToolsProcess(commands);
#pragma warning restore CA1859

        var result = tools.Run("component", "wit", "contract.wit");

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("wasm-tools", commands.Executable);
        var arguments = Assert.IsType<string[]>(commands.Arguments);
        Assert.Equal(["component", "wit", "contract.wit"], arguments);
        Assert.Throws<ArgumentNullException>(() => new WasmToolsProcess(null!));
    }

    [Fact]
    public void SystemRunnerExecutesAndRejectsInvalidCommands()
    {
#pragma warning disable CA1859 // Contract tests deliberately dispatch through the one-action interface.
        IExternalCommandRunner runner = new SystemExternalCommandRunner();
#pragma warning restore CA1859
        const string dotnet = "dotnet";

        var result = runner.Run(dotnet, ["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.StandardOutput);
        Assert.Throws<ArgumentException>(() => runner.Run(" ", []));
        Assert.Throws<ArgumentNullException>(() => runner.Run(dotnet, null!));
        Assert.Throws<WitBindingException>(() =>
            runner.Run("netwasm-command-that-does-not-exist", []));
    }

    private sealed class RecordingCommandRunner(ToolResult result) : IExternalCommandRunner
    {
        public string? Executable { get; private set; }

        public string[]? Arguments { get; private set; }

        public ToolResult Run(string executable, IEnumerable<string> arguments)
        {
            Executable = executable;
            Arguments = [.. arguments];
            return result;
        }
    }
}
