namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmBinaryWriterTests
{
    [Fact]
    public void WritesRawBytesIntoTheInjectedBuffer()
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        var reader = new WasmBinarySnapshotReader(buffer);

        writer.Write([0xaa, 0xbb]);

        Assert.Equal([0xaa, 0xbb], reader.Read());
    }

    [Fact]
    public void ReadsIndependentSnapshotsOfTheMutableBuffer()
    {
        var buffer = new WasmBinaryBuffer();
        var writer = new WasmBinaryWriter(buffer);
        var reader = new WasmBinarySnapshotReader(buffer);

        writer.Write([1]);
        var first = reader.Read();
        writer.Write([2]);

        Assert.Equal([1], first);
        Assert.Equal([1, 2], reader.Read());
    }
}
