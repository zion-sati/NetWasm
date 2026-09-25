using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilerRequestWriterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WritePersistsCompilerHostContract(WasmTarget target)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-request-writer-");
        try
        {
            var path = Path.Combine(directory.FullName, "request.json");
            var writer = Assert.IsAssignableFrom<ICorpusCompilerRequestWriter>(new CorpusCompilerRequestWriter());
            var request = CorpusApplicationCompilerTests.CreateRequest(target);

            writer.Write(path, request);

            using var actual = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(JsonSerializer.Serialize(request), actual.RootElement.GetRawText());
            Assert.Equal("same.dll", actual.RootElement.GetProperty("EntryAssemblyPath").GetString());
            Assert.Equal((int)target, actual.RootElement.GetProperty("Target").GetInt32());
            Assert.Equal("layout", actual.RootElement.GetProperty("RuntimeLayoutPath").GetString());
            Assert.Equal("interop", actual.RootElement.GetProperty("InteropManifestPath").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void WriteRejectsInvalidInputsAndPropagatesPersistenceFailure()
    {
        var writer = Assert.IsAssignableFrom<ICorpusCompilerRequestWriter>(new CorpusCompilerRequestWriter());
        var request = CorpusApplicationCompilerTests.CreateRequest(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, request));
        Assert.Throws<ArgumentException>(() => writer.Write(" ", request));
        Assert.Throws<ArgumentNullException>(() => writer.Write("unused", null!));
        var directory = Directory.CreateTempSubdirectory("netwasm-request-writer-");
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                writer.Write(Path.Combine(directory.FullName, "missing", "request.json"), request));
            Assert.Empty(directory.GetFileSystemInfos());
        }
        finally
        {
            directory.Delete();
        }
    }
}
