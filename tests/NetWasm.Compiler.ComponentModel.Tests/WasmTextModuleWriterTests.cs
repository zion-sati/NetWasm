using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WasmTextModuleWriterTests
{
    [Fact]
    public void ParsesTemporaryTextAndDeletesTheSource()
    {
        using var files = new ComponentModelTestFiles();
        var tools = new RecordingTools();
        var output = files.PathFor("module.wasm");

        new WasmTextModuleWriter(
            tools, new SystemTextFileWriter(), new SystemFileDeleter()).Write(
                "(module)", output);

        Assert.Equal("(module)", tools.Source);
        Assert.Equal(["parse", output + ".wat", "--output", output], tools.Arguments);
        Assert.False(File.Exists(output + ".wat"));
    }

    [Fact]
    public void ReportsToolFailureAndDeletesTheSource()
    {
        using var files = new ComponentModelTestFiles();
        var tools = new RecordingTools { Result = new(1, "", "parse failed\n") };
        var output = files.PathFor("module.wasm");

        var exception = Assert.Throws<CompilerException>(() =>
            new WasmTextModuleWriter(
                tools, new SystemTextFileWriter(), new SystemFileDeleter()).Write(
                    "(module)", output));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("parse failed", exception.Diagnostic.Message);
        Assert.False(File.Exists(output + ".wat"));
    }

    [Fact]
    public void RejectsBlankInputsAndNormalizesEmptyToolErrors()
    {
        using var files = new ComponentModelTestFiles();
        var output = files.PathFor("module.wasm");
        var writer = new WasmTextModuleWriter(
            new RecordingTools { Result = new(1, "", "") },
            new SystemTextFileWriter(),
            new SystemFileDeleter());

        Assert.Throws<ArgumentException>(() => writer.Write(" ", output));
        Assert.Throws<ArgumentException>(() => writer.Write("(module)", " "));
        var exception = Assert.Throws<CompilerException>(() => writer.Write(
            "(module)", output));
        Assert.Contains("unknown error", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    private sealed class RecordingTools : IWasmTools
    {
        public ToolResult Result { get; init; } = new(0, "", "");
        public string? Source { get; private set; }
        public string[] Arguments { get; private set; } = [];

        public ToolResult Run(params IEnumerable<string> arguments)
        {
            Arguments = arguments.ToArray();
            Source = File.ReadAllText(Arguments[1]);
            return Result;
        }
    }
}
