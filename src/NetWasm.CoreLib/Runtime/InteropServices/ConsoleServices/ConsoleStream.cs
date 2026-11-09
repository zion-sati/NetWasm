using System.IO;

namespace System.Runtime.InteropServices.ConsoleServices;

// Console.OpenStandardInput, OpenStandardOutput and OpenStandardError: an unseekable stream that
// only reads or only writes. Ported from System.Console's ConsoleStream; the standard streams
// below it are NetWasm's platform services.
internal abstract class ConsoleStream : Stream
{
    private bool _canRead;
    private bool _canWrite;

    private protected ConsoleStream(FileAccess access)
    {
        _canRead = (access & FileAccess.Read) == FileAccess.Read;
        _canWrite = (access & FileAccess.Write) == FileAccess.Write;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateWrite(buffer, offset, count);
        Write(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    public override void WriteByte(byte value) => Write(new ReadOnlySpan<byte>(in value));

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateRead(buffer, offset, count);
        return Read(new Span<byte>(buffer, offset, count));
    }

    public override int ReadByte()
    {
        byte value = 0;
        var result = Read(new Span<byte>(ref value));
        return result != 0 ? value : -1;
    }

    protected override void Dispose(bool disposing)
    {
        _canRead = false;
        _canWrite = false;
        base.Dispose(disposing);
    }

    public sealed override bool CanRead => _canRead;

    public sealed override bool CanWrite => _canWrite;

    public sealed override bool CanSeek => false;

    public sealed override long Length => throw new NotSupportedException(System.SR.NotSupported_UnseekableStream);

    public sealed override long Position
    {
        get => throw new NotSupportedException(System.SR.NotSupported_UnseekableStream);
        set => throw new NotSupportedException(System.SR.NotSupported_UnseekableStream);
    }

    public override void Flush()
    {
    }

    public sealed override void SetLength(long value) =>
        throw new NotSupportedException(System.SR.NotSupported_UnseekableStream);

    public sealed override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException(System.SR.NotSupported_UnseekableStream);

    private void ValidateRead(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (!_canRead)
        {
            throw new NotSupportedException(System.SR.NotSupported_UnreadableStream);
        }
    }

    private void ValidateWrite(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        if (!_canWrite)
        {
            throw new NotSupportedException(System.SR.NotSupported_UnwritableStream);
        }
    }
}

internal sealed class StandardInputStream(WebAssembly.IStandardStreams streams) : ConsoleStream(FileAccess.Read)
{
    private readonly WebAssembly.IStandardStreams _streams = streams ?? throw new ArgumentNullException(nameof(streams));

    public override int Read(Span<byte> buffer) => _streams.ReadInput(buffer);

    public override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException(System.SR.NotSupported_UnwritableStream);
}

internal sealed class StandardOutputStream(WebAssembly.IStandardStreams streams) : ConsoleStream(FileAccess.Write)
{
    private readonly WebAssembly.IStandardStreams _streams = streams ?? throw new ArgumentNullException(nameof(streams));

    public override int Read(Span<byte> buffer) =>
        throw new NotSupportedException(System.SR.NotSupported_UnreadableStream);

    public override void Write(ReadOnlySpan<byte> buffer) => _streams.WriteOutput(buffer);
}

internal sealed class StandardErrorStream(WebAssembly.IStandardStreams streams) : ConsoleStream(FileAccess.Write)
{
    private readonly WebAssembly.IStandardStreams _streams = streams ?? throw new ArgumentNullException(nameof(streams));

    public override int Read(Span<byte> buffer) =>
        throw new NotSupportedException(System.SR.NotSupported_UnreadableStream);

    public override void Write(ReadOnlySpan<byte> buffer) => _streams.WriteError(buffer);
}
