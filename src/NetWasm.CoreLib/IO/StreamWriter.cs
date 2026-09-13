// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). File/path members are
// intentionally omitted for the managed NetWasm profile; async members use
// TextWriter's scheduler-backed surface.

using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    public class StreamWriter : TextWriter
    {
        private const int DefaultBufferSize = 1024;
        private const int MinBufferSize = 128;

        public static new readonly StreamWriter Null = new NullStreamWriter();

        private readonly Stream _stream;
        private readonly Encoding _encoding;
        private readonly Encoder _encoder;
        private readonly char[] _charBuffer;
        private byte[]? _byteBuffer;
        private int _charPosition;
        private readonly int _charLength;
        private bool _autoFlush;
        private bool _haveWrittenPreamble;
        private readonly bool _closable;
        private bool _disposed;

        private static Encoding UTF8NoBOM => new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private StreamWriter()
        {
            _stream = Stream.Null;
            _encoding = UTF8NoBOM;
            _encoder = null!;
            _charBuffer = [];
            _charLength = 0;
            _closable = false;
        }

        public StreamWriter(Stream stream) : this(stream, UTF8NoBOM, DefaultBufferSize, false) { }
        public StreamWriter(Stream stream, Encoding? encoding) : this(stream, encoding, DefaultBufferSize, false) { }
        public StreamWriter(Stream stream, Encoding? encoding, int bufferSize) : this(stream, encoding, bufferSize, false) { }

        public StreamWriter(Stream stream, Encoding? encoding = null, int bufferSize = -1, bool leaveOpen = false) : base(null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanWrite)
            {
                throw new ArgumentException("The stream does not support writing.");
            }

            if (bufferSize == -1)
            {
                bufferSize = DefaultBufferSize;
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

            _stream = stream;
            _encoding = encoding ?? UTF8NoBOM;
            _encoder = _encoding.GetEncoder();
            _charLength = Math.Max(bufferSize, MinBufferSize);
            _charBuffer = new char[_charLength];
            if (_stream.CanSeek && _stream.Position > 0)
            {
                _haveWrittenPreamble = true;
            }
            _closable = !leaveOpen;
        }

        public override void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public override ValueTask DisposeAsync() => DisposeAsyncCore();

        private async ValueTask DisposeAsyncCore()
        {
            try
            {
                if (!_disposed)
                {
                    await FlushAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (_closable && !_disposed)
                    {
                        await _stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    _disposed = true;
                    base.Dispose(true);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!_disposed && disposing)
                {
                    Flush(flushStream: true, flushEncoder: true);
                }
            }
            finally
            {
                if (_closable && !_disposed)
                {
                    try
                    {
                        if (disposing)
                        {
                            _stream.Close();
                        }
                    }
                    finally
                    {
                        _disposed = true;
                        base.Dispose(disposing);
                    }
                }
                else
                {
                    _disposed = true;
                    base.Dispose(disposing);
                }
            }
        }

        public override void Flush() => Flush(flushStream: true, flushEncoder: true);

        private void Flush(bool flushStream, bool flushEncoder)
        {
            ThrowIfDisposed();
            if (_charPosition == 0 && !flushStream && !flushEncoder)
            {
                return;
            }

            if (!_haveWrittenPreamble)
            {
                _haveWrittenPreamble = true;
                var preamble = _encoding.Preamble;
                if (preamble.Length > 0)
                {
                    var bytes = preamble.ToArray();
                    _stream.Write(bytes, 0, bytes.Length);
                }
            }

            if (_byteBuffer is null || _byteBuffer.Length < _encoding.GetMaxByteCount(_charPosition))
            {
                _byteBuffer = new byte[_encoding.GetMaxByteCount(_charBuffer.Length)];
            }

            var count = _encoder.GetBytes(_charBuffer, 0, _charPosition, _byteBuffer, 0, flushEncoder);
            _charPosition = 0;
            if (count > 0)
            {
                _stream.Write(_byteBuffer, 0, count);
            }
            if (flushStream)
            {
                _stream.Flush();
            }
        }

        public virtual bool AutoFlush
        {
            get => _autoFlush;
            set
            {
                ThrowIfDisposed();
                _autoFlush = value;
                if (value)
                {
                    Flush(true, false);
                }
            }
        }

        public virtual Stream BaseStream => _stream;
        public override Encoding Encoding => _encoding;

        public override void Write(char value)
        {
            ThrowIfDisposed();
            if (_charPosition == _charLength)
            {
                Flush(false, false);
            }

            _charBuffer[_charPosition++] = value;
            if (_autoFlush)
            {
                Flush(true, false);
            }
        }

        public override void Write(char[]? buffer)
        {
            if (buffer is not null)
            {
                Write(buffer, 0, buffer.Length);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            WriteSpan(buffer.AsSpan(index, count), appendNewLine: false);
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (GetType() != typeof(StreamWriter))
            {
                base.Write(buffer);
                return;
            }

            WriteSpan(buffer, appendNewLine: false);
        }

        public override void Write(string? value)
        {
            if (value is not null)
            {
                WriteSpan(value.AsSpan(), appendNewLine: false);
            }
        }

        public override void WriteLine(string? value)
        {
            WriteSpan(value.AsSpan(), appendNewLine: true);
        }

        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            if (GetType() != typeof(StreamWriter))
            {
                base.WriteLine(buffer);
                return;
            }

            WriteSpan(buffer, appendNewLine: true);
        }

        private void WriteSpan(ReadOnlySpan<char> buffer, bool appendNewLine)
        {
            ThrowIfDisposed();
            var copied = 0;
            while (copied < buffer.Length)
            {
                if (_charPosition == _charLength)
                {
                    Flush(false, false);
                }

                var count = Math.Min(_charLength - _charPosition, buffer.Length - copied);
                buffer.Slice(copied, count).CopyTo(new Span<char>(_charBuffer, _charPosition, count));
                _charPosition += count;
                copied += count;
            }

            if (appendNewLine)
            {
                foreach (var character in CoreNewLine)
                {
                    if (_charPosition == _charLength)
                    {
                        Flush(false, false);
                    }
                    _charBuffer[_charPosition++] = character;
                }
            }

            if (_autoFlush)
            {
                Flush(true, false);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null, "Cannot write to a closed writer.");
            }
        }

        private sealed class NullStreamWriter : StreamWriter
        {
            internal NullStreamWriter() { }
            public override void Flush() { }
            public override void Write(char value) { }
            public override void Write(char[]? buffer) { }
            public override void Write(char[] buffer, int index, int count) { }
            public override void Write(ReadOnlySpan<char> buffer) { }
            public override void Write(string? value) { }
            public override void WriteLine(string? value) { }
            public override void WriteLine(ReadOnlySpan<char> buffer) { }
            public override Task WriteAsync(char value) => Task.CompletedTask;
            public override Task WriteAsync(string? value) => Task.CompletedTask;
            public override Task WriteAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteAsync(char[] buffer, int index, int count) => Task.CompletedTask;
            public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteLineAsync(char value) => Task.CompletedTask;
            public override Task WriteLineAsync(string? value) => Task.CompletedTask;
            public override Task WriteLineAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteLineAsync(char[] buffer, int index, int count) => Task.CompletedTask;
            public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteLineAsync() => Task.CompletedTask;
            public override Task FlushAsync() => Task.CompletedTask;
            public override Task FlushAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
            protected override void Dispose(bool disposing) { }
        }
    }
}
