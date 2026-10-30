// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 474ed963699fc29809015392738a15231aaae2de). File/path members are
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
        private int _charLength;
        private bool _autoFlush;
        private bool _haveWrittenPreamble;
        private readonly bool _closable;
        private bool _disposed;
        private bool _asyncIOInProgress;

        private static Encoding UTF8NoBOM => EncodingCache.UTF8NoBOM;

        private void CheckAsyncTaskInProgress()
        {
            if (_asyncIOInProgress)
                throw new InvalidOperationException("The stream is currently in use by a previous operation.");
        }

        private ThrowOnWritesScope GuardAgainstOtherWrites() => new(this);

        private readonly struct ThrowOnWritesScope : IDisposable
        {
            private readonly StreamWriter _writer;

            internal ThrowOnWritesScope(StreamWriter writer)
            {
                writer._asyncIOInProgress = true;
                _writer = writer;
            }

            public void Dispose() => _writer._asyncIOInProgress = false;
        }

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

        public override ValueTask DisposeAsync() =>
            GetType() != typeof(StreamWriter) ? base.DisposeAsync() : DisposeAsyncCore();

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
                CloseStreamFromDispose(disposing: true);
            }
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!_disposed && disposing)
                {
                    CheckAsyncTaskInProgress();
                    Flush(flushStream: true, flushEncoder: true);
                }
            }
            finally
            {
                CloseStreamFromDispose(disposing);
            }
        }

        private void CloseStreamFromDispose(bool disposing)
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
                    _charLength = 0;
                    base.Dispose(disposing);
                }
            }
        }

        public override void Flush()
        {
            CheckAsyncTaskInProgress();
            Flush(flushStream: true, flushEncoder: true);
        }

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
                CheckAsyncTaskInProgress();
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
            CheckAsyncTaskInProgress();
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
            CheckAsyncTaskInProgress();
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

        public override Task WriteAsync(char value)
        {
            if (GetType() != typeof(StreamWriter))
                return base.WriteAsync(value);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return WriteAsyncInternal(value, appendNewLine: false);
        }

        private async Task WriteAsyncInternal(char value, bool appendNewLine)
        {
            using ThrowOnWritesScope _ = GuardAgainstOtherWrites();
            if (_charPosition == _charLength)
                await FlushAsyncInternal(flushStream: false, flushEncoder: false).ConfigureAwait(false);

            _charBuffer[_charPosition++] = value;
            if (appendNewLine)
            {
                foreach (var character in CoreNewLine)
                {
                    if (_charPosition == _charLength)
                        await FlushAsyncInternal(flushStream: false, flushEncoder: false).ConfigureAwait(false);
                    _charBuffer[_charPosition++] = character;
                }
            }

            if (_autoFlush)
                await FlushAsyncInternal(flushStream: true, flushEncoder: false).ConfigureAwait(false);
        }

        public override Task WriteAsync(string? value)
        {
            if (GetType() != typeof(StreamWriter))
                return base.WriteAsync(value);
            if (value is null)
                return Task.CompletedTask;

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return WriteAsyncInternal(value.AsMemory(), appendNewLine: false, default);
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            if (GetType() != typeof(StreamWriter))
                return base.WriteAsync(buffer, index, count);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return WriteAsyncInternal(new ReadOnlyMemory<char>(buffer, index, count), appendNewLine: false, default);
        }

        public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (GetType() != typeof(StreamWriter))
                return base.WriteAsync(buffer, cancellationToken);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            return WriteAsyncInternal(buffer, appendNewLine: false, cancellationToken);
        }

        private async Task WriteAsyncInternal(
            ReadOnlyMemory<char> source,
            bool appendNewLine,
            CancellationToken cancellationToken)
        {
            using ThrowOnWritesScope _ = GuardAgainstOtherWrites();
            var copied = 0;
            while (copied < source.Length)
            {
                if (_charPosition == _charLength)
                    await FlushAsyncInternal(flushStream: false, flushEncoder: false, cancellationToken).ConfigureAwait(false);

                var count = Math.Min(_charLength - _charPosition, source.Length - copied);
                source.Span.Slice(copied, count).CopyTo(new Span<char>(_charBuffer, _charPosition, count));
                _charPosition += count;
                copied += count;
            }

            if (appendNewLine)
            {
                foreach (var character in CoreNewLine)
                {
                    if (_charPosition == _charLength)
                        await FlushAsyncInternal(flushStream: false, flushEncoder: false, cancellationToken).ConfigureAwait(false);
                    _charBuffer[_charPosition++] = character;
                }
            }

            if (_autoFlush)
                await FlushAsyncInternal(flushStream: true, flushEncoder: false, cancellationToken).ConfigureAwait(false);
        }

        public override Task WriteLineAsync()
        {
            if (GetType() != typeof(StreamWriter))
                return base.WriteLineAsync();
            return StartWriteLineAsync(ReadOnlyMemory<char>.Empty, default);
        }

        public override Task WriteLineAsync(char value)
        {
            if (GetType() != typeof(StreamWriter))
                return base.WriteLineAsync(value);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return WriteAsyncInternal(value, appendNewLine: true);
        }

        public override Task WriteLineAsync(string? value)
        {
            if (value is null)
                return WriteLineAsync();
            if (GetType() != typeof(StreamWriter))
                return base.WriteLineAsync(value);
            return StartWriteLineAsync(value.AsMemory(), default);
        }

        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            if (GetType() != typeof(StreamWriter))
                return base.WriteLineAsync(buffer, index, count);
            return StartWriteLineAsync(new ReadOnlyMemory<char>(buffer, index, count), default);
        }

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (GetType() != typeof(StreamWriter))
                return base.WriteLineAsync(buffer, cancellationToken);
            return StartWriteLineAsync(buffer, cancellationToken);
        }

        private Task StartWriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            return WriteAsyncInternal(buffer, appendNewLine: true, cancellationToken);
        }

        public override Task FlushAsync()
        {
            if (GetType() != typeof(StreamWriter))
                return base.FlushAsync();

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return FlushAsyncInternalWithGuard(
                flushStream: true,
                flushEncoder: true,
                CancellationToken.None);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (GetType() != typeof(StreamWriter))
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled(cancellationToken);
                return FlushAsync();
            }

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return FlushAsyncInternalWithGuard(flushStream: true, flushEncoder: true, cancellationToken);
        }

        private async Task FlushAsyncInternalWithGuard(
            bool flushStream,
            bool flushEncoder,
            CancellationToken cancellationToken)
        {
            using ThrowOnWritesScope _ = GuardAgainstOtherWrites();
            await FlushAsyncInternal(flushStream, flushEncoder, cancellationToken).ConfigureAwait(false);
        }

        private Task FlushAsyncInternal(
            bool flushStream,
            bool flushEncoder,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            if (_charPosition == 0 && !flushStream && !flushEncoder)
                return Task.CompletedTask;

            return Core(flushStream, flushEncoder, cancellationToken);

            async Task Core(bool flushUnderlyingStream, bool flushEncoding, CancellationToken token)
            {
                if (!_haveWrittenPreamble)
                {
                    _haveWrittenPreamble = true;
                    var preamble = _encoding.GetPreamble();
                    if (preamble.Length > 0)
                        await _stream.WriteAsync(preamble, token).ConfigureAwait(false);
                }

                var bytes = _byteBuffer ??= new byte[_encoding.GetMaxByteCount(_charBuffer.Length)];
                var count = _encoder.GetBytes(
                    new ReadOnlySpan<char>(_charBuffer, 0, _charPosition),
                    bytes,
                    flushEncoding);
                _charPosition = 0;
                if (count > 0)
                    await _stream.WriteAsync(new ReadOnlyMemory<byte>(bytes, 0, count), token).ConfigureAwait(false);
                if (flushUnderlyingStream)
                    await _stream.FlushAsync(token).ConfigureAwait(false);
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
            public override bool AutoFlush { get => false; set { } }
            [Diagnostics.CodeAnalysis.AllowNull]
            public override string NewLine { get => base.NewLine; set { } }
            public override IFormatProvider FormatProvider => Globalization.CultureInfo.InvariantCulture;

            public override void Close() { }
            protected override void Dispose(bool disposing) { }
            public override ValueTask DisposeAsync() => default;
            public override void Flush() { }
            public override Task FlushAsync() => Task.CompletedTask;
            public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public override void Write(char value) { }
            public override void Write(Text.Rune value) { }
            public override void Write(char[]? buffer) { }
            public override void Write(char[] buffer, int index, int count) { }
            public override void Write(ReadOnlySpan<char> buffer) { }
            public override void Write(bool value) { }
            public override void Write(int value) { }
            public override void Write(uint value) { }
            public override void Write(long value) { }
            public override void Write(ulong value) { }
            public override void Write(float value) { }
            public override void Write(double value) { }
            public override void Write(decimal value) { }
            public override void Write(string? value) { }
            public override void Write(object? value) { }
            public override void Write(Text.StringBuilder? value) { }
            public override void Write(string format, object? arg0) { }
            public override void Write(string format, object? arg0, object? arg1) { }
            public override void Write(string format, object? arg0, object? arg1, object? arg2) { }
            public override void Write(string format, params object?[] arg) { }
            public override void Write(string format, params ReadOnlySpan<object?> arg) { }
            public override void WriteLine(string? value) { }
            public override void WriteLine() { }
            public override void WriteLine(char value) { }
            public override void WriteLine(Text.Rune value) { }
            public override void WriteLine(char[]? buffer) { }
            public override void WriteLine(char[] buffer, int index, int count) { }
            public override void WriteLine(ReadOnlySpan<char> buffer) { }
            public override void WriteLine(bool value) { }
            public override void WriteLine(int value) { }
            public override void WriteLine(uint value) { }
            public override void WriteLine(long value) { }
            public override void WriteLine(ulong value) { }
            public override void WriteLine(float value) { }
            public override void WriteLine(double value) { }
            public override void WriteLine(decimal value) { }
            public override void WriteLine(Text.StringBuilder? value) { }
            public override void WriteLine(object? value) { }
            public override void WriteLine(string format, object? arg0) { }
            public override void WriteLine(string format, object? arg0, object? arg1) { }
            public override void WriteLine(string format, object? arg0, object? arg1, object? arg2) { }
            public override void WriteLine(string format, params object?[] arg) { }
            public override void WriteLine(string format, params ReadOnlySpan<object?> arg) { }
            public override Task WriteAsync(char value) => Task.CompletedTask;
            public override Task WriteAsync(Text.Rune value) => Task.CompletedTask;
            public override Task WriteAsync(string? value) => Task.CompletedTask;
            public override Task WriteAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteAsync(char[] buffer, int index, int count) => Task.CompletedTask;
            public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteLineAsync(char value) => Task.CompletedTask;
            public override Task WriteLineAsync(Text.Rune value) => Task.CompletedTask;
            public override Task WriteLineAsync(string? value) => Task.CompletedTask;
            public override Task WriteLineAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteLineAsync(char[] buffer, int index, int count) => Task.CompletedTask;
            public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public override Task WriteLineAsync() => Task.CompletedTask;
        }
    }
}
