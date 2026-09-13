using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Tests.TestSupport;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Tests.Artifacts;

public sealed class CompilerArtifactWriterTests
{
    [Fact]
    public void WritePersistsTheCompilerArtifacts()
    {
        using var directory = new TestDirectory();
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            new CompilerArtifactWriter());
        var compilation = CompilerTaskTestData.CreateCompilation(
            entryPoint: new(
                ManagedExecutableParameterShape.StringArray,
                ManagedExecutableReturnShape.ExitCode),
            runtimeFeatures: [NetWasmRuntimeFeatureIds.LocalTime],
            functionImports:
            [
                new("host", "call", WasmFunctionType.Create(
                    CliValueKind.I4,
                    CliValueKind.ManagedAddress)),
            ]);
        var coreModulePath = directory.PathTo("application.core.wasm");
        var layoutPath = directory.PathTo("runtime-layout.json");
        var interopPath = directory.PathTo("interop.json");
        var compilerMetadataPath = directory.PathTo("compiler-metadata.json");

        writer.Write(new(
            compilation,
            coreModulePath,
            layoutPath,
            interopPath,
            compilerMetadataPath,
            null));

        Assert.Equal(compilation.CoreModule, File.ReadAllBytes(coreModulePath));
        var layoutText = File.ReadAllText(layoutPath);
        Assert.Contains('\n', layoutText);
        Assert.DoesNotContain('\r', layoutText);
        using var layout = JsonDocument.Parse(layoutText);
        Assert.Equal(2, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("wasm32", layout.RootElement.GetProperty("target").GetString());
        Assert.Equal(65_537, layout.RootElement.GetProperty("applicationStaticDataEnd").GetInt32());
        Assert.False(layout.RootElement.TryGetProperty("runtimeGlobalBase", out _));
        var entryPoint = layout.RootElement.GetProperty("managedExecutableEntryPoint");
        Assert.Equal("stringArray", entryPoint.GetProperty("parameterShape").GetString());
        Assert.Equal("exitCode", entryPoint.GetProperty("returnShape").GetString());
        var interopText = File.ReadAllText(interopPath);
        Assert.Contains('\n', interopText);
        Assert.DoesNotContain('\r', interopText);
        using var interop = JsonDocument.Parse(interopText);
        Assert.Equal(1, interop.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("wasm32", interop.RootElement.GetProperty("target").GetString());
        using var compilerMetadata = JsonDocument.Parse(File.ReadAllText(compilerMetadataPath));
        Assert.Equal(1, compilerMetadata.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("wasm32", compilerMetadata.RootElement.GetProperty("target").GetString());
        Assert.Equal("local-time", Assert.Single(
            compilerMetadata.RootElement.GetProperty("runtimeFeatures").EnumerateArray()).GetString());
        var functionImport = Assert.Single(
            compilerMetadata.RootElement.GetProperty("functionImports").EnumerateArray());
        Assert.Equal("host", functionImport.GetProperty("module").GetString());
        Assert.Equal("call", functionImport.GetProperty("name").GetString());
        Assert.Equal("managedAddress", Assert.Single(
            functionImport.GetProperty("parameters").EnumerateArray()).GetString());
        Assert.Equal("i4", functionImport.GetProperty("result").GetString());
    }

    [Fact]
    public void WritePersistsTheRequestedStackTraceSymbols()
    {
        using var directory = new TestDirectory();
        var symbols = new StackTraceSymbolArtifact(
            [1, 2, 3],
            "application/json",
            "application.netwasm.stacktrace.json",
            "sha256");
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            new CompilerArtifactWriter());
        var symbolsPath = directory.PathTo("symbols/application.stacktrace.json");

        writer.Write(new(
            CompilerTaskTestData.CreateCompilation(symbols: symbols),
            directory.PathTo("application.core.wasm"),
            directory.PathTo("runtime-layout.json"),
            directory.PathTo("interop.json"),
            directory.PathTo("compiler-metadata.json"),
            symbolsPath));

        Assert.Equal(symbols.Bytes, File.ReadAllBytes(symbolsPath));
    }

    [Fact]
    public void WriteRejectsMissingRequestedStackTraceSymbols()
    {
        using var directory = new TestDirectory();
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            new CompilerArtifactWriter());

        Assert.Throws<InvalidOperationException>(() => writer.Write(new(
            CompilerTaskTestData.CreateCompilation(),
            directory.PathTo("application.core.wasm"),
            directory.PathTo("runtime-layout.json"),
            directory.PathTo("interop.json"),
            directory.PathTo("compiler-metadata.json"),
            directory.PathTo("application.stacktrace.json"))));
    }

    [Fact]
    public void WriteRejectsANullRequest()
    {
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            new CompilerArtifactWriter());

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-compiler-tasks-{Guid.NewGuid():N}");

        public TestDirectory() => Directory.CreateDirectory(_path);

        public string PathTo(string relativePath) => System.IO.Path.Combine(_path, relativePath);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
