using System.Collections.Frozen;
using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompilerCliTests
{
    private const string Banner = "Copyright (C) 2026 Zion Sati. All rights reserved.";

    [Fact]
    public void RoutesANamedCommandWithItsOwnOptions()
    {
        var compile = new RecordingCommand(string.Empty, "out");
        var componentize = new RecordingCommand("componentize", "coremodule");
        var cli = Create(new RecordingConsole(), compile, componentize);

        var result = cli.Run(["componentize", "-nologo", "-coremodule:app.wasm"]);

        Assert.Equal(17, result);
        Assert.Equal("app.wasm", componentize.Arguments!.Options[^1].Value);
        Assert.Null(compile.Arguments);
    }

    [Theory]
    [InlineData("App.dll")]
    [InlineData("")]
    public void RoutesEverythingElseToCompile(string first)
    {
        var compile = new RecordingCommand(string.Empty, "out");
        var cli = Create(new RecordingConsole(), compile, new RecordingCommand("componentize"));

        cli.Run([first, "-nologo"]);

        Assert.Equal([first], compile.Arguments!.Inputs);
    }

    [Fact]
    public void PrintsTheBannerFirstUnlessNoLogo()
    {
        var console = new RecordingConsole();
        var compile = new RecordingCommand(string.Empty);
        var files = new InMemoryTextFiles(new() { ["quiet.rsp"] = "-nologo" });
        var cli = new CompilerCli([compile], files, console, NwcTestArguments.ConsoleLogger<CompilerCli>(console));

        cli.Run([]);
        var bannerLines = console.Lines.ToArray();
        cli.Run(["-nologo"]);
        cli.Run(["@quiet.rsp"]);

        Assert.Equal(3, bannerLines.Length);
        Assert.StartsWith("NetWasm Compiler version ", bannerLines[0], StringComparison.Ordinal);
        Assert.Equal([Banner, string.Empty], bannerLines[1..]);
        Assert.Equal(bannerLines, console.Lines);
    }

    [Fact]
    public void ReportsEveryArgumentErrorAfterTheBannerWithoutRunningTheCommand()
    {
        var console = new RecordingConsole();
        var compile = new RecordingCommand(string.Empty);
        var cli = Create(console, compile);

        var result = cli.Run(["-unknown", "@missing.rsp", "-help"]);

        Assert.Equal(1, result);
        Assert.Null(compile.Arguments);
        Assert.Equal(
            [
                Banner,
                string.Empty,
                "error NW1000: unrecognized option '-unknown'",
                "error NW1000: cannot read response file 'missing.rsp': Could not find file 'missing.rsp'.",
            ],
            console.Lines[1..]);
    }

    [Theory]
    [InlineData("-help")]
    [InlineData("/?")]
    public void HelpSummarizesTheOptionsWithoutRunningTheCommand(string help)
    {
        var console = new RecordingConsole();
        var componentize = new RecordingCommand("componentize");
        var cli = Create(console, new RecordingCommand(string.Empty), componentize);

        var result = cli.Run(["componentize", "-nologo", help]);

        Assert.Equal(0, result);
        Assert.Null(componentize.Arguments);
        Assert.Equal("                             NetWasm Compiler Options", console.Lines[0]);
    }

    [Fact]
    public void ACommonFlagTakesNoValue()
    {
        var cli = Create(new RecordingConsole(), new RecordingCommand(string.Empty));

        var exception = Assert.Throws<CompilerException>(() => cli.Run(["-nologo:yes"]));

        Assert.Equal("option '-nologo:yes' takes no value", exception.Diagnostic.Message);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var console = new RecordingConsole();
        var files = new InMemoryTextFiles();
        var logger = NwcTestArguments.ConsoleLogger<CompilerCli>(console);

        Assert.Throws<ArgumentNullException>(() => Create(console).Run(null!));
        Assert.Throws<ArgumentNullException>(() => new CompilerCli([], null!, console, logger));
        Assert.Throws<ArgumentNullException>(() => new CompilerCli([], files, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new CompilerCli([], files, console, null!));
    }

    private static CompilerCli Create(RecordingConsole console, params ICompilerCliCommand[] commands) =>
        new(commands, new InMemoryTextFiles(), console, NwcTestArguments.ConsoleLogger<CompilerCli>(console));

    private sealed class RecordingCommand(string name, params string[] options) : ICompilerCliCommand
    {
        public string Name => name;

        public FrozenSet<string> Options { get; } = options.ToFrozenSet(StringComparer.Ordinal);

        public NwcArguments? Arguments { get; private set; }

        public int Run(NwcArguments arguments)
        {
            Arguments = arguments;
            return 17;
        }
    }
}
