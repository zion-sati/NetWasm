using System.Text.Json;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeLayoutReaderTests
{
    [Fact]
    public void ReadsCompilerRuntimeLayoutEvidence()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("runtime-layout.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            target = "wasm64",
            applicationStaticDataEnd = 65_537,
        }));

        var reader = Assert.IsAssignableFrom<IRuntimeLayoutReader>(new RuntimeLayoutReader());
        var layout = reader.Read(path);

        Assert.Equal(2, layout.SchemaVersion);
        Assert.Equal("wasm64", layout.Target);
        Assert.Equal(65_537, layout.ApplicationStaticDataEnd);
    }

    [Fact]
    public void RejectsMissingLayoutEvidence()
    {
        using var directory = new TemporaryDirectory();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.PathTo("missing.json")));
        Assert.Equal("The NetWasm runtime layout evidence is missing.", exception.Message);
    }

    [Fact]
    public void RejectsMalformedLayoutEvidence()
    {
        using var directory = new TemporaryDirectory();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", "{")));
        Assert.Equal("The NetWasm runtime layout evidence is malformed.", exception.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0}")]
    [InlineData("{\"schemaVersion\":2,\"target\":\" \",\"applicationStaticDataEnd\":0}")]
    [InlineData("{\"schemaVersion\":2,\"target\":\"wasm32\",\"applicationStaticDataEnd\":-1}")]
    public void RejectsInvalidLayoutEvidence(string content)
    {
        using var directory = new TemporaryDirectory();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", content)));
        Assert.Equal("The NetWasm runtime layout evidence is invalid.", exception.Message);
    }

    [Fact]
    public void RejectsMissingPath() =>
        Assert.Throws<ArgumentException>(() => new RuntimeLayoutReader().Read(" "));
}
