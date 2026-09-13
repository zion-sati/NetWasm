using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Cli;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompilerCliTests
{
    [Fact]
    public void RoutesNamedCommandsWithoutTheirCommandToken()
    {
        var compile = new RecordingCommand(CompileCliCommand.CommandName);
        var componentize = new RecordingCommand("componentize");
        var cli = new CompilerCli([compile, componentize]);

        var result = cli.Run(["componentize", "--wit", "contract.wit"]);

        Assert.Equal(17, result);
        Assert.NotNull(componentize.Arguments);
        Assert.Equal(["--wit", "contract.wit"], componentize.Arguments);
        Assert.Null(compile.Arguments);
    }

    [Fact]
    public void RoutesUnprefixedArgumentsToTheCompileCommand()
    {
        var compile = new RecordingCommand(CompileCliCommand.CommandName);
        var cli = new CompilerCli([compile]);

        cli.Run(["--input", "application.dll"]);

        Assert.NotNull(compile.Arguments);
        Assert.Equal(["--input", "application.dll"], compile.Arguments);
    }

    [Fact]
    public void RejectsNullArguments()
    {
        var cli = new CompilerCli([]);

        Assert.Throws<ArgumentNullException>(() => cli.Run(null!));
    }

    [Fact]
    public void RegistersCommandsAndCapabilities()
    {
        var services = new ServiceCollection();
        services.AddNetWasmCompilerCli();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ITextFileReader));
    }

    [Fact]
    public void ProgramConvertsCompilerDiagnosticsToExitCode()
    {
        var exitCode = Program.Main(["--unknown", "value"]);

        Assert.Equal(1, exitCode);
    }

    private sealed class RecordingCommand(string name) : ICompilerCliCommand
    {
        public string Name => name;
        public string[]? Arguments { get; private set; }

        public int Run(string[] arguments)
        {
            Arguments = arguments;
            return 17;
        }
    }
}
