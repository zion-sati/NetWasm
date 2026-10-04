using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeArtifactReaderTests
{
    [Fact]
    public void ReadsCurrentOwnedBytesAndPreservesMissingArtifactFailure()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteBytes("runtime.wasm", [1, 2, 3]);
        var reader = Assert.IsAssignableFrom<IRuntimeArtifactReader>(new RuntimeArtifactReader());
        Assert.Equal([1, 2, 3], reader.Read(path));
        File.WriteAllBytes(path, [4, 5]);
        Assert.Equal([4, 5], reader.Read(path));
        Assert.Throws<FileNotFoundException>(() => reader.Read(directory.PathTo("missing.wasm")));
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
    }
}
