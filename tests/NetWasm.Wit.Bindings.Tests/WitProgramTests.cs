using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitProgramTests
{
    [Fact]
    public void MainRunsTheComposedCommandAndReportsBindingDiagnostics()
    {
        var command = new RecordingCommand(7);

        Assert.Equal(7, Program.Run(["--wit", "contract.wit"], () => command));
        Assert.NotNull(command.Arguments);
        Assert.Equal(["--wit", "contract.wit"], command.Arguments);

        var originalError = Console.Error;
        using var diagnostics = new StringWriter();
        Console.SetError(diagnostics);
        try
        {
            Assert.Equal(
                1,
                Program.Run([], static () => new ThrowingCommand()));
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("binding failure", diagnostics.ToString(),
            StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => Program.Run(null!, () => command));
        Assert.Throws<ArgumentNullException>(() => Program.Run([], null!));
    }

    private sealed class RecordingCommand(int exitCode) : IWitBindingCommand
    {
        public string[]? Arguments { get; private set; }

        public int Run(string[] arguments)
        {
            Arguments = arguments;
            return exitCode;
        }
    }

    private sealed class ThrowingCommand : IWitBindingCommand
    {
        public int Run(string[] arguments) =>
            throw WitBindingException.Invalid("binding failure");
    }
}
