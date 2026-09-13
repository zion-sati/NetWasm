using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.Tests.Artifacts;

public sealed class CompilerBuildMetadataReaderTests
{
    [Fact]
    public void ReadsExactCanonicalMetadata()
    {
        using var file = new TemporaryJsonFile("""
            {
              "schemaVersion": 1,
              "target": "wasm64",
              "runtimeFeatures": ["local-time"],
              "functionImports": [
                {
                  "module": "host",
                  "name": "call",
                  "parameters": ["managedAddress", "i4"],
                  "result": "i4"
                }
              ]
            }
            """);

        var metadata = AsReader(new CompilerBuildMetadataReader()).Read(file.Path);

        Assert.Equal(1, metadata.SchemaVersion);
        Assert.Equal("wasm64", metadata.Target);
        Assert.Equal([NetWasmRuntimeFeatureIds.LocalTime], metadata.RuntimeFeatures.ToArray());
        var import = Assert.Single(metadata.FunctionImports);
        Assert.Equal("host", import.Module);
        Assert.Equal("call", import.Name);
        Assert.Equal([CliValueKind.ManagedAddress, CliValueKind.I4],
            import.Type.Parameters.ToArray());
        Assert.Equal(CliValueKind.I4, import.Type.Result);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"target\":\"wasm32\",\"runtimeFeatures\":[],\"functionImports\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"unknown\",\"runtimeFeatures\":[],\"functionImports\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"wasm32\",\"runtimeFeatures\":[\"future\"],\"functionImports\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"wasm32\",\"runtimeFeatures\":[],\"functionImports\":[],\"extra\":true}")]
    public void RejectsUnsupportedOrNonCanonicalMetadata(string json)
    {
        using var file = new TemporaryJsonFile(json);

        Assert.ThrowsAny<Exception>(() =>
            AsReader(new CompilerBuildMetadataReader()).Read(file.Path));
    }

    private static ICompilerBuildMetadataReader AsReader(object reader) =>
        (ICompilerBuildMetadataReader)reader;

    private sealed class TemporaryJsonFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-compiler-metadata-{Guid.NewGuid():N}");

        public TemporaryJsonFile(string contents)
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "metadata.json");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
