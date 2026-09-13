using NetWasm.Compiler.Cli;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompileCliOptionsTests
{
    [Fact]
    public void ParsesTargetReferencesExportsAndComponentSelection()
    {
        var options = CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", "wasm64",
            "--reference", "first.dll",
            "--reference", "second.dll",
            "--export", "other=Example.Entry::Other",
            "--wit", "contract.wit",
            "--world", "example:test/test",
            "--diagnostic-trace", "trace.json",
            "--diagnostic-log", "compiler.jsonl",
            "--source", "Entry.cs",
            "--source", "Helpers.cs",
        ]);

        Assert.Equal(WasmTarget.Wasm64, options.Target);
        Assert.Equal(["first.dll", "second.dll"], options.References);
        var export = Assert.Single(options.Exports);
        Assert.Equal("other", export.Name);
        Assert.Equal("Example.Entry", export.TypeName);
        Assert.Equal("Other", export.MethodName);
        Assert.Equal("contract.wit", options.Wit);
        Assert.Equal("trace.json", options.DiagnosticTrace);
        Assert.Equal("compiler.jsonl", options.DiagnosticLog);
        Assert.Equal(["Entry.cs", "Helpers.cs"], options.Sources);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("name=")]
    public void RejectsMalformedExports(string export)
    {
        var exception = Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--export", export,
        ]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMalformedEntryAndUnsupportedTarget()
    {
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Malformed"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", "wasm128"]));
    }

    [Fact]
    public void RejectsUnknownOptionMissingValueAndRequiredFields()
    {
        Assert.Throws<ArgumentNullException>(() => CompileCliOptions.Parse(null!));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse(["--unknown", "value"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse(["--input"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--output", "application.wasm"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--output", "application.wasm", "--entry", "Example.Entry::Run"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--entry", "Example.Entry::Run"]));
        Assert.Throws<CompilerException>(() => CompileCliOptions.Parse([
            "--input", "application.dll", "--output", "application.wasm",
            "--entry", "Example.Entry::Run", "--input", "duplicate.dll"]));
    }

    [Fact]
    public void OptionReaderRejectsNullCallbacksAndTrailingOptions()
    {
        Assert.Throws<ArgumentNullException>(() => CliOptionReader.Read(null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => CliOptionReader.Read([], null!));
        Assert.Throws<CompilerException>(() => CliOptionReader.Read(["--input"], (_, _) => { }));
    }
}
