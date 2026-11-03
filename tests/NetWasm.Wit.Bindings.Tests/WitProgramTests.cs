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
                Program.Run(["--wit", "contract.wit"], static () => new ThrowingCommand()));
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

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void HelpPrintsTheUsageWithoutRunningTheCommand(string option)
    {
        var command = new RecordingCommand(7);
        var (exitCode, output, error) = Capture(() => Program.Run(["--wit", "a.wit", option], () => command));

        Assert.Equal(0, exitCode);
        Assert.Null(command.Arguments);
        Assert.Empty(error);
        Assert.StartsWith("Usage: netwasm-wit-bindgen --wit <path> --output <path> [options]", output,
            StringComparison.Ordinal);
        foreach (var name in new[] { "--wit", "--output", "--world", "--namespace", "--accessibility" })
        {
            Assert.Contains("  " + name + " <", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEmptyCommandLineShowsTheUsageAndFails()
    {
        var command = new RecordingCommand(7);
        var (exitCode, output, error) = Capture(() => Program.Run([], () => command));

        Assert.Equal(1, exitCode);
        Assert.Null(command.Arguments);
        Assert.Empty(output);
        Assert.StartsWith("Usage: netwasm-wit-bindgen", error, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyCommandLineErrorsPointAtTheHelp()
    {
        var (usageExit, _, usageError) = Capture(() => Program.Run(["--wit"],
            static () => new WitBindingCommandForOptions()));
        var (bindingExit, _, bindingError) = Capture(() => Program.Run(["--wit", "a.wit"],
            static () => new ThrowingCommand()));

        Assert.Equal(1, usageExit);
        Assert.Equal("missing value for option '--wit'" + Environment.NewLine +
            "Run 'netwasm-wit-bindgen --help' for usage." + Environment.NewLine, usageError);
        Assert.Equal(1, bindingExit);
        Assert.DoesNotContain("--help", bindingError, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) Capture(Func<int> run)
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return (run(), output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    // Reads the command line as the binding command does, and stops there.
    private sealed class WitBindingCommandForOptions : IWitBindingCommand
    {
        public int Run(string[] arguments)
        {
            new WitBindingOptionsReader().Read(arguments);
            return 0;
        }
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
