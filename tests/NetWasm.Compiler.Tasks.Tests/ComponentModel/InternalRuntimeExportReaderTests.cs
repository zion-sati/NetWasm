using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class InternalRuntimeExportReaderTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[]")]
    public void ReadsAnEmptyRemovalPlan(string metadata) => Assert.Empty(new InternalRuntimeExportReader().Read(metadata));

    [Fact]
    public void ReadsExactTemporaryNamesAndKinds()
    {
        var exports = new InternalRuntimeExportReader().Read("""
            [{"Name":"native","Kind":0},{"Name":"__heap_base","Kind":3}]
            """);
        Assert.Equal([new WasmInternalExport("native", 0), new("__heap_base", 3)], exports.ToArray());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"Name\":\"n\"}]")]
    [InlineData("[{\"Kind\":0}]")]
    [InlineData("[{\"Name\":42,\"Kind\":0}]")]
    [InlineData("[{\"Name\":\"n\",\"Kind\":\"0\"}]")]
    [InlineData("[{\"Name\":\"n\",\"Kind\":256}]")]
    [InlineData("[{\"Name\":\"n\",\"Kind\":2}]")]
    [InlineData("[{\"Name\":\" \",\"Kind\":0}]")]
    [InlineData("[{\"Name\":\"n\\n\",\"Kind\":0}]")]
    [InlineData("[{\"Name\":\"n\",\"Kind\":0},{\"Name\":\"n\",\"Kind\":0}]")]
    [InlineData("[{\"Name\":\"n\",\"Name\":\"other\",\"Kind\":0}]")]
    public void RejectsIncompleteContradictoryOrUnsupportedMetadata(string metadata) =>
        Assert.Throws<InvalidOperationException>(() => new InternalRuntimeExportReader().Read(metadata));

    [Fact]
    public void PreservesMalformedJsonCause()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new InternalRuntimeExportReader().Read("["));
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(error.InnerException);
    }
}
