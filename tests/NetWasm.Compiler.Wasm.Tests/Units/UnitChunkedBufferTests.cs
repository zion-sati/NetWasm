using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A segment's sections grow in chunks rather than one array (issue 229); what they write reads back
// as one sequence.
public sealed class UnitChunkedBufferTests
{
    [Fact]
    public void WritesAcrossChunksAndReadsBackInOrder()
    {
        var buffer = new UnitChunkedBuffer<byte>(4);
        byte[] values = [.. Enumerable.Range(1, 10).Select(static value => (byte)value)];

        buffer.Write(values.AsSpan(0, 3));
        buffer.Write(values.AsSpan(3));

        Assert.Equal(10, buffer.WrittenCount);
        Assert.Equal(values, Bytes(buffer));
    }

    // A span is contiguous in one chunk, so one larger than a chunk gets its own, and what the
    // last chunk held stays ahead of it.
    [Fact]
    public void ASpanLongerThanAChunkIsContiguous()
    {
        var buffer = new UnitChunkedBuffer<byte>(4);
        buffer.Write([1, 2]);

        var span = buffer.GetSpan(6);
        for (var index = 0; index < 6; index++) span[index] = (byte)(10 + index);
        buffer.Advance(6);
        buffer.Write([99]);

        Assert.Equal(9, buffer.WrittenCount);
        Assert.Equal([1, 2, 10, 11, 12, 13, 14, 15, 99], Bytes(buffer));
    }

    [Fact]
    public void CharactersWriteAsUtf16()
    {
        var buffer = new UnitChunkedBuffer<char>(2);

        buffer.Write("abc");

        Assert.Equal(3, buffer.WrittenCount);
        Assert.Equal("abc", System.Text.Encoding.Unicode.GetString(Bytes(buffer)));
    }

    [Fact]
    public void RejectsInvalidLengthsAndDestinations()
    {
        var buffer = new UnitChunkedBuffer<byte>(4);
        buffer.GetSpan(2);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetSpan(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Advance(5));
        Assert.Throws<ArgumentNullException>(() => buffer.WriteTo(null!));
        Assert.Equal(0, buffer.WrittenCount);
    }

    private static byte[] Bytes<T>(UnitChunkedBuffer<T> buffer) where T : unmanaged
    {
        using var stream = new MemoryStream();
        buffer.WriteTo(stream);
        return stream.ToArray();
    }
}
