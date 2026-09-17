using NetWasm.Compiler.Cli;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class ComponentManifestInputReaderTests
{
    [Fact]
    public void MapsTheDirectJavaScriptBoundary()
    {
        var path = "interop.json";
        var files = new RecordingTextFileReader("""
                {
                  "version": 1,
                  "target": "wasm32",
                  "imports": [{
                    "module": "consumer.math", "name": "add",
                    "parameters": ["i32", "i32"], "result": "i32"
                  }],
                  "exports": [{
                    "name": "run", "parameters": ["i32"], "result": "i32"
                  }]
                }
                """);
        var options = Options(path, WasmTarget.Wasm32);

        var result = new ComponentManifestInputReader(files).Read(options);

        Assert.Equal(path, files.Path);
        Assert.Equal("consumer.math", Assert.Single(result.JavaScript.Imports).Module);
        Assert.Equal("run", Assert.Single(result.JavaScript.Exports).Name);
    }

    [Fact]
    public void RejectsAnInteropManifestForAnotherTarget()
    {
        var path = "interop.json";
        var files = new RecordingTextFileReader("""
                { "version": 1, "target": "wasm64", "imports": [], "exports": [] }
                """);

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentManifestInputReader(files).Read(
                Options(path, WasmTarget.Wasm32)));

        Assert.Contains("does not match", exception.Diagnostic.Message);
    }

    [Fact]
    public void OmitsBoundaryWhenInteropManifestWasNotRequested()
    {
        var result = new ComponentManifestInputReader(
            new RecordingTextFileReader("unused"))
            .Read(new ComponentizeCliOptions(
                "application.wasm",
                null,
                "contract.wit",
                null,
                "component.wasm",
                "component.json",
                WasmTarget.Wasm32,
                null,
                null,
                null,
                FinalWasmOptimization.Size));

        Assert.Empty(result.JavaScript.Imports);
        Assert.Empty(result.JavaScript.Exports);
    }

    [Fact]
    public void RejectsEmptyInteropManifest()
    {
        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentManifestInputReader(new RecordingTextFileReader("null"))
                .Read(Options("interop.json", WasmTarget.Wasm32)));

        Assert.Contains("is empty", exception.Diagnostic.Message);
    }

    [Fact]
    public void AcceptsMatchingWasm64InteropManifest()
    {
        var reader = new ComponentManifestInputReader(new RecordingTextFileReader("""
            { "version": 1, "target": "wasm64", "imports": [], "exports": [] }
            """));

        var result = reader.Read(Options("interop.json", WasmTarget.Wasm64));

        Assert.Empty(result.JavaScript.Imports);
    }

    [Fact]
    public void RejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentManifestInputReader(new RecordingTextFileReader("{}"))
                .Read(null!));
    }

    [Fact]
    public void RejectsMissingTextFileCapability()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ComponentManifestInputReader(null!));
    }

    private static ComponentizeCliOptions Options(
        string interopManifest,
        WasmTarget target) => new(
            "application.wasm",
            "runtime.wasm",
            "contract.wit",
            null,
            "component.wasm",
            "component.json",
            target,
            interopManifest,
            null,
            null,
            FinalWasmOptimization.Size);

    private sealed class RecordingTextFileReader(string content) : ITextFileReader
    {
        public string? Path { get; private set; }

        public string Read(string path)
        {
            Path = path;
            return content;
        }
    }
}
