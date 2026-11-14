using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class CommandInvokerTests
{
    [Fact]
    public void InvokesToolAndPersistsItsOutput()
    {
        using var directory = new TemporaryDirectory();
        var command = new RuntimeCommand(
            NetWasm.TestInfrastructure.DotNetHost.Executable,
            ImmutableArray.Create("--version"),
            directory.PathTo("logs/tool.log"));

        var invoker = Assert.IsAssignableFrom<ICommandInvoker>(new CommandInvoker());
        invoker.Invoke(command);

        Assert.NotEmpty(File.ReadAllText(command.LogPath));
    }

    [Fact]
    public void ReportsToolFailureThroughLogPath()
    {
        using var directory = new TemporaryDirectory();
        var command = new RuntimeCommand(
            NetWasm.TestInfrastructure.DotNetHost.Executable,
            ImmutableArray.Create("--definitely-invalid-netwasm-option"),
            directory.PathTo("logs/tool.log"));

        var exception = Assert.Throws<InvalidOperationException>(() => new CommandInvoker().Invoke(command));

        Assert.Contains(Path.GetFullPath(command.LogPath), exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(command.LogPath));
    }

    // NWPACK001 shows only the message, so it names the tool and why it could not start (zion-sati/NetWasm#236).
    [Fact]
    public void ReportsToolStartFailureWithItsCause()
    {
        using var directory = new TemporaryDirectory();
        var command = new RuntimeCommand(
            directory.PathTo("missing-tool"),
            [],
            directory.PathTo("logs/tool.log"));

        var exception = Assert.Throws<InvalidOperationException>(() => new CommandInvoker().Invoke(command));

        Assert.StartsWith($"A NetWasm runtime tool could not be executed ('{command.ExecutablePath}'): ", exception.Message,
            StringComparison.Ordinal);
        Assert.EndsWith(exception.InnerException!.Message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNullCommand() =>
        Assert.Throws<ArgumentNullException>(() => new CommandInvoker().Invoke(null!));
}
