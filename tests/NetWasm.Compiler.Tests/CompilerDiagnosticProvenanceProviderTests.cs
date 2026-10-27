using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticProvenanceProviderTests
{
    [Fact]
    public void RecordsSourcesHashesToolsAndAQuotedReproductionCommand()
    {
        using var assets = TestAssets.Create();
        var source = Path.Combine(assets.Directory, "source with quote'.cs");
        File.WriteAllText(source, "public static class Fixture { }");
        var options = CompilerTestSupport.Options(
            assets,
            [new RequestedExport("public", "Some.Type", "Method")]) with
        {
            SourcePaths = [source, source],
            WitPath = "contract path/world.wit",
            WitWorld = "example-world",
        };
        var files = new CompilerDiagnosticFileExistenceReader();
        var sourcePaths = new CompilerSourcePathProvider(files);
        var commandBuilder = new CompilerReproductionCommandBuilder(sourcePaths);
        var provenanceProvider = new CompilerDiagnosticProvenanceProvider(
            new RecordingToolRunner(),
            files,
            new CompilerDiagnosticFileByteReader());

        var sources = sourcePaths.Provide(options);
        var command = commandBuilder.Build(options);
        var provenance = JsonSerializer.Serialize(provenanceProvider.ProvideProvenance(options));

        Assert.Equal(Path.GetFullPath(source), Assert.Single(sources));
        Assert.Contains("-source:", command);
        Assert.Contains("'\"'\"'", command);
        Assert.Contains("-wit:", command);
        Assert.Contains("-world:", command);
        Assert.Contains(
            Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(options.EntryAssemblyPath))),
            provenance);
        Assert.Contains("wasm-tools", provenance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoversAnAdjacentSourceWhenNoSourceListWasProvided()
    {
        using var assets = TestAssets.Create();
        var assembly = Path.Combine(assets.Directory, "Adjacent.dll");
        var source = Path.ChangeExtension(assembly, ".cs");
        File.WriteAllBytes(assembly, [1]);
        File.WriteAllText(source, "source");
        var options = new CompilerOptions(assembly, [], "Entry", "Run", []);

        var sources = new CompilerSourcePathProvider(
            new CompilerDiagnosticFileExistenceReader()).Provide(options);

        Assert.Equal(Path.GetFullPath(source), Assert.Single(sources));
    }

    [Fact]
    public void OmitsToolVersionWhenTheToolRunnerFails()
    {
        using var assets = TestAssets.Create();
        var files = new CompilerDiagnosticFileExistenceReader();
        var provider = new CompilerDiagnosticProvenanceProvider(
            new FailingToolRunner(),
            files,
            new CompilerDiagnosticFileByteReader());

        var provenance = JsonSerializer.Serialize(provider.ProvideProvenance(
            CompilerTestSupport.Options(assets, [])));

        using var document = JsonDocument.Parse(provenance);
        Assert.Equal(JsonValueKind.Null,
            document.RootElement.GetProperty("WasmTools").ValueKind);
    }

    [Fact]
    public void RecordsToolErrorAndNullHashesForMissingAssemblies()
    {
        var provider = new CompilerDiagnosticProvenanceProvider(
            new ErrorToolRunner(),
            new MissingFiles(),
            new EmptyBytes());
        var options = new CompilerOptions(
            "missing.dll",
            ["reference.dll"],
            "Entry",
            "Run",
            []);

        var provenance = JsonSerializer.Serialize(provider.ProvideProvenance(options));

        Assert.Contains("tool unavailable", provenance, StringComparison.Ordinal);
        Assert.Contains("sha256", provenance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", provenance, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingToolRunner : IExternalToolRunner
    {
        public ToolResult Run(string executable, IEnumerable<string> arguments) =>
            new(0, "wasm-tools 1.0", "");
    }

    private sealed class FailingToolRunner : IExternalToolRunner
    {
        public ToolResult Run(string executable, IEnumerable<string> arguments) =>
            throw new InvalidOperationException("not installed");
    }

    private sealed class ErrorToolRunner : IExternalToolRunner
    {
        public ToolResult Run(string executable, IEnumerable<string> arguments) =>
            new(7, "", "tool unavailable");
    }

    private sealed class MissingFiles : ICompilerDiagnosticFileExistenceReader
    {
        public bool Exists(string path) => false;
    }

    private sealed class EmptyBytes : ICompilerDiagnosticFileByteReader
    {
        public byte[] ReadBytes(string path) => [];
    }
}
