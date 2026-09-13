using Xunit;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class StackTraceCliOptionsTests
{
    [Fact]
    public void ParsesStackTraceSymbolSidecarPath()
    {
        var options = CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--stack-trace-symbols", "application.netwasm.stacktrace.json",
        ]);

        Assert.Equal(
            "application.netwasm.stacktrace.json",
            options.StackTraceSymbols);
    }
}
