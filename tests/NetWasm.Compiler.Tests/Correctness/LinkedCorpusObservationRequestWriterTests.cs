using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusObservationRequestWriterTests
{
    [Fact]
    public void WritePersistsCamelCaseJavaScriptContract()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-linked-request-");
        try
        {
            var path = Path.Combine(directory.FullName, "request.json");
            var writer = Assert.IsAssignableFrom<ILinkedCorpusObservationRequestWriter>(new LinkedCorpusObservationRequestWriter());
            writer.Write(path, LinkedCorpusObservationResponseParserTests.CreateRequest());
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("wasm32", document.RootElement.GetProperty("target").GetString());
            Assert.Equal([-1, 0, 1], document.RootElement.GetProperty("inputs").EnumerateArray().Select(item => item.GetInt32()));
            Assert.False(document.RootElement.TryGetProperty("SchemaVersion", out _));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void WriteRejectsInvalidInputsAndPropagatesFailure()
    {
        var writer = Assert.IsAssignableFrom<ILinkedCorpusObservationRequestWriter>(new LinkedCorpusObservationRequestWriter());
        var request = LinkedCorpusObservationResponseParserTests.CreateRequest();
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, request));
        Assert.Throws<ArgumentException>(() => writer.Write(" ", request));
        Assert.Throws<ArgumentNullException>(() => writer.Write("unused", null!));
        var directory = Directory.CreateTempSubdirectory("netwasm-linked-request-");
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() => writer.Write(
                Path.Combine(directory.FullName, "missing", "request.json"), request));
            Assert.Empty(directory.GetFileSystemInfos());
        }
        finally
        {
            directory.Delete();
        }
    }
}
