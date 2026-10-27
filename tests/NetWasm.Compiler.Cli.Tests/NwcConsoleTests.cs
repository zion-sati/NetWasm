using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Cli.Tests.TestSupport;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class NwcConsoleTests
{
    [Theory]
    [InlineData(NwcConsoleLoggerProvider.ProgressCategory, LogLevel.Information, true)]
    [InlineData("NetWasm.Compiler.Cli.CompilerCli", LogLevel.Error, true)]
    [InlineData("NetWasm.Compiler.Cli.CompilerCli", LogLevel.Debug, false)]
    [InlineData("NetWasm.Compiler.Diagnostics.CompilerDiagnosticFileLogger", LogLevel.Information, false)]
    [InlineData("NetWasm.Compiler.Clinic", LogLevel.Error, false)]
    [InlineData(null, LogLevel.Critical, false)]
    public void ShowsOnlyNwcEntriesAndTheCompilerProgress(string? category, LogLevel level, bool expected) =>
        Assert.Equal(expected, NwcConsoleLoggerProvider.Shows(category, level));

    [Theory]
    [InlineData(LogLevel.Information, "NetWasm: Compiling (2.0s elapsed)")]
    [InlineData(LogLevel.Warning, "warning NW1003: NetWasm: Compiling (2.0s elapsed)")]
    [InlineData(LogLevel.Error, "error NW1003: NetWasm: Compiling (2.0s elapsed)")]
    [InlineData(LogLevel.Critical, "error NW1003: NetWasm: Compiling (2.0s elapsed)")]
    public void WritesEachEntryAsCscWritesIt(LogLevel level, string expected)
    {
        var console = new RecordingConsole();
        using var provider = new NwcConsoleLoggerProvider(console);
        var logger = provider.CreateLogger("NetWasm.Compiler.Cli.Test");

        logger.Log(level, new EventId(1003), "NetWasm: Compiling (2.0s elapsed)", null, static (text, _) => text);

        Assert.Equal([expected], console.Lines);
    }

    [Fact]
    public void NamesTheCodeAnEventCarries()
    {
        var console = new RecordingConsole();
        using var provider = new NwcConsoleLoggerProvider(console);

        provider.CreateLogger("NetWasm.Compiler.Cli.Test")
            .Log(LogLevel.Error, new EventId(2001, "NWA2001"), "expansion", null, static (text, _) => text);

        Assert.Equal(["error NWA2001: expansion"], console.Lines);
    }

    [Fact]
    public void EnablesInformationAndAboveWithoutScopes()
    {
        using var provider = new NwcConsoleLoggerProvider(new RecordingConsole());
        var logger = provider.CreateLogger("NetWasm.Compiler.Cli.Test");

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.Null(logger.BeginScope("scope"));
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        using var provider = new NwcConsoleLoggerProvider(new RecordingConsole());
        var logger = provider.CreateLogger("NetWasm.Compiler.Cli.Test");

        Assert.Throws<ArgumentNullException>(() => new NwcConsoleLoggerProvider(null!));
        Assert.Throws<ArgumentNullException>(() =>
            logger.Log<string>(LogLevel.Error, default, "text", null, null!));
    }

    [Theory]
    [InlineData("1.2.3+0123456789abcdef", "NetWasm Compiler version 1.2.3 (01234567)")]
    [InlineData("1.2.3+abc", "NetWasm Compiler version 1.2.3 (abc)")]
    [InlineData("1.2.3", "NetWasm Compiler version 1.2.3")]
    public void TheBannerNamesTheVersionAndSourceCommit(string version, string expected)
    {
        var console = new RecordingConsole();

        NwcBanner.Write(console, version);

        Assert.Equal([expected, "Copyright (C) 2026 Zion Sati. All rights reserved.", string.Empty], console.Lines);
    }

    [Fact]
    public void TheBannerReadsTheCompilerVersion()
    {
        Assert.NotEmpty(NwcBanner.InformationalVersion());
        Assert.Throws<ArgumentNullException>(() => NwcBanner.Write(null!, "1.0.0"));
    }

    [Fact]
    public void HelpListsEveryOption()
    {
        var console = new RecordingConsole();

        NwcHelp.Write(console);

        var help = string.Join('\n', console.Lines);
        var options = CompileCliOptions.Names
            .Where(name => !name.StartsWith("managed", StringComparison.Ordinal))
            .Concat(ComponentizeCliOptions.Names)
            .Concat(NwcArgumentReader.CommonOptions);
        Assert.All(options, name => Assert.Contains($"-{name}", help, StringComparison.Ordinal));
        Assert.Contains("-managed<optimization>:<value>", help, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemConsoleWritesToStandardOutput()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            new SystemConsole().WriteLine("line");
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal("line" + Environment.NewLine, output.ToString());
    }
}
