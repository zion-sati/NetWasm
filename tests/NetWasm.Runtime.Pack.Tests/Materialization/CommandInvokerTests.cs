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
            ResolveDotNet(),
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
            ResolveDotNet(),
            ImmutableArray.Create("--definitely-invalid-netwasm-option"),
            directory.PathTo("logs/tool.log"));

        var exception = Assert.Throws<InvalidOperationException>(() => new CommandInvoker().Invoke(command));

        Assert.Contains(Path.GetFullPath(command.LogPath), exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(command.LogPath));
    }

    [Fact]
    public void ReportsToolStartFailureWithoutRawProcessOutput()
    {
        using var directory = new TemporaryDirectory();
        var command = new RuntimeCommand(
            directory.PathTo("missing-tool"),
            [],
            directory.PathTo("logs/tool.log"));

        var exception = Assert.Throws<InvalidOperationException>(() => new CommandInvoker().Invoke(command));

        Assert.Equal("A NetWasm runtime tool could not be executed.", exception.Message);
    }

    [Fact]
    public void RejectsNullCommand() =>
        Assert.Throws<ArgumentNullException>(() => new CommandInvoker().Invoke(null!));

    private static string ResolveDotNet() =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "dotnet");
}
