// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 474ed963699fc29809015392738a15231aaae2de). File/path members are
// intentionally omitted for the managed NetWasm profile; async members are
// inherited from TextReader's scheduler-backed surface.

using System.Buffers.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    public class StreamReader : TextReader
    {
        public static new readonly StreamReader Null = new NullStreamReader();

        private const int DefaultBufferSize = 1024;
        private const int MinBufferSize = 128;

        private readonly Stream _stream;
        private Encoding _encoding = null!;
        private Decoder _decoder = null!;
        private readonly byte[] _byteBuffer = null!;
        private char[] _charBuffer = null!;
        private int _charPosition;
        private int _charLength;
        private int _byteLength;
        private int _bytePosition;
        private int _maxCharsPerBuffer;
        private bool _disposed;
        private bool _detectEncoding;
        private bool _checkPreamble;
        private bool _isBlocked;
        private readonly bool _closable;
        private bool _asyncIOInProgress;

        private void CheckAsyncTaskInProgress()
        {
            if (_asyncIOInProgress)
                throw new InvalidOperationException("The stream is currently in use by a previous operation.");
        }

        private ThrowOnReadsScope GuardAgainstOtherReads() => new(this);

        private readonly struct ThrowOnReadsScope : IDisposable
        {
            private readonly StreamReader _reader;

            internal ThrowOnReadsScope(StreamReader reader)
            {
                reader._asyncIOInProgress = true;
                _reader = reader;
            }

            public void Dispose() => _reader._asyncIOInProgress = false;
        }

        private StreamReader()
        {
            _stream = Stream.Null;
            _encoding = Encoding.Unicode;
            _decoder = _encoding.GetDecoder();
            _byteBuffer = new byte[1];
            _charBuffer = new char[1];
            _maxCharsPerBuffer = 1;
            _detectEncoding = false;
            _checkPreamble = false;
            _closable = false;
        }

        public StreamReader(Stream stream) : this(stream, Encoding.UTF8, true, DefaultBufferSize, false) { }
        public StreamReader(Stream stream, bool detectEncodingFromByteOrderMarks) : this(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks, DefaultBufferSize, false) { }
        public StreamReader(Stream stream, Encoding? encoding) : this(stream, encoding, true, DefaultBufferSize, false) { }
        public StreamReader(Stream stream, Encoding? encoding, bool detectEncodingFromByteOrderMarks) : this(stream, encoding, detectEncodingFromByteOrderMarks, DefaultBufferSize, false) { }
        public StreamReader(Stream stream, Encoding? encoding, bool detectEncodingFromByteOrderMarks, int bufferSize) : this(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize, false) { }

        public StreamReader(Stream stream, Encoding? encoding = null, bool detectEncodingFromByteOrderMarks = true, int bufferSize = -1, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead)
            {
                throw new ArgumentException("The stream does not support reading.");
            }

            if (bufferSize == -1)
            {
                bufferSize = DefaultBufferSize;
            }
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

            _stream = stream;
            _encoding = encoding ?? Encoding.UTF8;
            _decoder = _encoding.GetDecoder();
            bufferSize = Math.Max(bufferSize, MinBufferSize);
            _byteBuffer = new byte[bufferSize];
            _maxCharsPerBuffer = _encoding.GetMaxCharCount(bufferSize);
            _charBuffer = new char[_maxCharsPerBuffer];
            _detectEncoding = detectEncodingFromByteOrderMarks;
            var preambleLength = _encoding.Preamble.Length;
            _checkPreamble = preambleLength > 0 && preambleLength <= bufferSize;
            _closable = !leaveOpen;
        }

        public override void Close() => Dispose(true);

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (disposing && _closable)
                {
                    _stream.Close();
                }
            }
            finally
            {
                _charPosition = 0;
                _charLength = 0;
                base.Dispose(disposing);
            }
        }

        public virtual Encoding CurrentEncoding => _encoding;
        public virtual Stream BaseStream => _stream;

        public void DiscardBufferedData()
        {
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            _byteLength = 0;
            _bytePosition = 0;
            _charLength = 0;
            _charPosition = 0;
            _decoder = _encoding.GetDecoder();
            _isBlocked = false;
        }

        public bool EndOfStream
        {
            get
            {
                CheckAsyncTaskInProgress();
                ThrowIfDisposed();
                if (_charPosition < _charLength)
                {
                    return false;
                }

                return ReadBuffer() == 0;
            }
        }

        public override int Peek()
        {
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            if (_charPosition == _charLength && ReadBuffer() == 0)
            {
                return -1;
            }

            return _charBuffer[_charPosition];
        }

        public override int Read()
        {
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            if (_charPosition == _charLength && ReadBuffer() == 0)
            {
                return -1;
            }

            return _charBuffer[_charPosition++];
        }

        public override int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            return Read(buffer.AsSpan(index, count));
        }

        public override int Read(Span<char> buffer) =>
            GetType() == typeof(StreamReader) ? ReadSpan(buffer) : base.Read(buffer);

        private int ReadSpan(Span<char> buffer)
        {
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            var total = 0;
            var remaining = buffer.Length;
            while (remaining > 0)
            {
                var available = _charLength - _charPosition;
                if (available == 0)
                {
                    available = ReadBuffer();
                }
                if (available == 0)
                {
                    break;
                }

                var take = Math.Min(available, remaining);
                new Span<char>(_charBuffer, _charPosition, take).CopyTo(buffer.Slice(total, take));
                _charPosition += take;
                total += take;
                remaining -= take;
                if (_isBlocked)
                {
                    break;
                }
            }

            return total;
        }

        public override string ReadToEnd()
        {
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            var builder = new Text.StringBuilder(_charLength - _charPosition);
            do
            {
                builder.Append(_charBuffer, _charPosition, _charLength - _charPosition);
                _charPosition = _charLength;
                ReadBuffer();
            }
            while (_charLength > 0);

            return builder.ToString();
        }

        public override int ReadBlock(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            return base.ReadBlock(buffer, index, count);
        }

        public override int ReadBlock(Span<char> buffer)
        {
            if (GetType() != typeof(StreamReader))
            {
                return base.ReadBlock(buffer);
            }

            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            var total = 0;
            int read;
            do
            {
                read = ReadSpan(buffer.Slice(total));
                total += read;
            }
            while (read > 0 && total < buffer.Length);

            return total;
        }

        public override string? ReadLine()
        {
            CheckAsyncTaskInProgress();
            ThrowIfDisposed();
            if (_charPosition == _charLength && ReadBuffer() == 0)
            {
                return null;
            }

            var builder = new Text.StringBuilder();
            while (true)
            {
                var start = _charPosition;
                while (_charPosition < _charLength && _charBuffer[_charPosition] is not '\r' and not '\n')
                {
                    _charPosition++;
                }

                builder.Append(new ReadOnlySpan<char>(_charBuffer, start, _charPosition - start));
                if (_charPosition < _charLength)
                {
                    var terminator = _charBuffer[_charPosition++];
                    if (terminator == '\r')
                    {
                        if (_charPosition == _charLength)
                        {
                            ReadBuffer();
                        }
                        if (_charPosition < _charLength && _charBuffer[_charPosition] == '\n')
                        {
                            _charPosition++;
                        }
                    }

                    return builder.ToString();
                }

                if (ReadBuffer() == 0)
                {
                    return builder.ToString();
                }
            }
        }

        public override Task<string?> ReadLineAsync() => ReadLineAsync(default).AsTask();

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (GetType() != typeof(StreamReader))
                return base.ReadLineAsync(cancellationToken);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return new ValueTask<string?>(ReadLineAsyncInternal(cancellationToken));
        }

        private async Task<string?> ReadLineAsyncInternal(CancellationToken cancellationToken)
        {
            using ThrowOnReadsScope _ = GuardAgainstOtherReads();
            if (_charPosition == _charLength &&
                await ReadBufferAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                return null;
            }

            var builder = new Text.StringBuilder();
            while (true)
            {
                var start = _charPosition;
                while (_charPosition < _charLength &&
                    _charBuffer[_charPosition] is not '\r' and not '\n')
                {
                    _charPosition++;
                }

                builder.Append(new ReadOnlySpan<char>(_charBuffer, start, _charPosition - start));
                if (_charPosition < _charLength)
                {
                    var terminator = _charBuffer[_charPosition++];
                    if (terminator == '\r')
                    {
                        if (_charPosition == _charLength)
                            await ReadBufferAsync(cancellationToken).ConfigureAwait(false);
                        if (_charPosition < _charLength && _charBuffer[_charPosition] == '\n')
                            _charPosition++;
                    }
                    return builder.ToString();
                }

                if (await ReadBufferAsync(cancellationToken).ConfigureAwait(false) == 0)
                    return builder.ToString();
            }
        }

        public override Task<string> ReadToEndAsync() => ReadToEndAsync(default);

        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            if (GetType() != typeof(StreamReader))
                return base.ReadToEndAsync(cancellationToken);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return ReadToEndAsyncInternal(cancellationToken);
        }

        private async Task<string> ReadToEndAsyncInternal(CancellationToken cancellationToken)
        {
            using ThrowOnReadsScope _ = GuardAgainstOtherReads();
            var builder = new Text.StringBuilder(_charLength - _charPosition);
            do
            {
                builder.Append(_charBuffer, _charPosition, _charLength - _charPosition);
                _charPosition = _charLength;
                await ReadBufferAsync(cancellationToken).ConfigureAwait(false);
            }
            while (_charLength > 0);
            return builder.ToString();
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            if (GetType() != typeof(StreamReader))
                return base.ReadAsync(buffer, index, count);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return ReadAsyncWithGuard(new Memory<char>(buffer, index, count), default);
        }

        private async Task<int> ReadAsyncWithGuard(Memory<char> buffer, CancellationToken cancellationToken)
        {
            using ThrowOnReadsScope _ = GuardAgainstOtherReads();
            return await ReadAsyncInternal(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            if (GetType() != typeof(StreamReader))
                return base.ReadAsync(buffer, cancellationToken);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);
            return ReadAsyncInternal(buffer, cancellationToken);
        }

        internal override async ValueTask<int> ReadAsyncInternal(
            Memory<char> buffer,
            CancellationToken cancellationToken)
        {
            if (_charPosition == _charLength &&
                await ReadBufferAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                return 0;
            }

            var charsRead = 0;
            var count = buffer.Length;
            var readToUserBuffer = false;
            while (count > 0)
            {
                var available = _charLength - _charPosition;
                if (available == 0)
                {
                    _charLength = 0;
                    _charPosition = 0;
                    if (!_checkPreamble)
                        _byteLength = 0;
                    readToUserBuffer = count >= _maxCharsPerBuffer;

                    do
                    {
                        if (_checkPreamble)
                        {
                            var read = await _stream.ReadAsync(
                                new Memory<byte>(_byteBuffer, _bytePosition, _byteBuffer.Length - _bytePosition),
                                cancellationToken).ConfigureAwait(false);
                            if (read == 0)
                            {
                                if (_byteLength > 0)
                                {
                                    if (readToUserBuffer)
                                    {
                                        available = _decoder.GetChars(
                                            new ReadOnlySpan<byte>(_byteBuffer, 0, _byteLength),
                                            buffer.Span.Slice(charsRead),
                                            flush: false);
                                        _charLength = 0;
                                    }
                                    else
                                    {
                                        available = _decoder.GetChars(
                                            _byteBuffer, 0, _byteLength, _charBuffer, 0);
                                        _charLength += available;
                                    }
                                }
                                _isBlocked = true;
                                break;
                            }
                            _byteLength += read;
                        }
                        else
                        {
                            _byteLength = await _stream.ReadAsync(
                                new Memory<byte>(_byteBuffer), cancellationToken).ConfigureAwait(false);
                            if (_byteLength == 0)
                            {
                                _isBlocked = true;
                                break;
                            }
                        }

                        _isBlocked = _byteLength < _byteBuffer.Length;
                        if (IsPreamble())
                            continue;
                        if (_detectEncoding && _byteLength >= 2)
                        {
                            DetectEncoding();
                            readToUserBuffer = count >= _maxCharsPerBuffer;
                        }

                        _charPosition = 0;
                        if (readToUserBuffer)
                        {
                            available = _decoder.GetChars(
                                new ReadOnlySpan<byte>(_byteBuffer, 0, _byteLength),
                                buffer.Span.Slice(charsRead),
                                flush: false);
                            _charLength = 0;
                        }
                        else
                        {
                            available = _decoder.GetChars(_byteBuffer, 0, _byteLength, _charBuffer, 0);
                            _charLength += available;
                        }
                    }
                    while (available == 0);

                    if (available == 0)
                        break;
                }

                if (available > count)
                    available = count;
                if (!readToUserBuffer)
                {
                    new Span<char>(_charBuffer, _charPosition, available)
                        .CopyTo(buffer.Span.Slice(charsRead));
                    _charPosition += available;
                }
                charsRead += available;
                count -= available;
                if (_isBlocked)
                    break;
            }
            return charsRead;
        }

        public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            if (GetType() != typeof(StreamReader))
                return base.ReadBlockAsync(buffer, index, count);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            return ReadBlockAsyncWithGuard(new Memory<char>(buffer, index, count), default).AsTask();
        }

        public override ValueTask<int> ReadBlockAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            if (GetType() != typeof(StreamReader))
                return base.ReadBlockAsync(buffer, cancellationToken);

            ThrowIfDisposed();
            CheckAsyncTaskInProgress();
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);
            return ReadBlockAsyncWithGuard(buffer, cancellationToken);
        }

        private async ValueTask<int> ReadBlockAsyncWithGuard(
            Memory<char> buffer,
            CancellationToken cancellationToken)
        {
            using ThrowOnReadsScope _ = GuardAgainstOtherReads();
            return await ReadBlockAsyncInternal(buffer, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<int> ReadBufferAsync(CancellationToken cancellationToken)
        {
            _charLength = 0;
            _charPosition = 0;
            if (!_checkPreamble)
                _byteLength = 0;

            var eof = false;
            do
            {
                if (_checkPreamble)
                {
                    var read = await _stream.ReadAsync(
                        _byteBuffer.AsMemory(_bytePosition), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        eof = true;
                        break;
                    }
                    _byteLength += read;
                }
                else
                {
                    _byteLength = await _stream.ReadAsync(
                        new Memory<byte>(_byteBuffer), cancellationToken).ConfigureAwait(false);
                    if (_byteLength == 0)
                    {
                        eof = true;
                        break;
                    }
                }

                _isBlocked = _byteLength < _byteBuffer.Length;
                if (IsPreamble())
                    continue;
                if (_detectEncoding && _byteLength >= 2)
                    DetectEncoding();
                _charLength = _decoder.GetChars(
                    _byteBuffer, 0, _byteLength, _charBuffer, 0, flush: false);
            }
            while (_charLength == 0);

            if (eof)
            {
                _charLength = _decoder.GetChars(
                    _byteBuffer, 0, _byteLength, _charBuffer, 0, flush: true);
                _bytePosition = 0;
                _byteLength = 0;
            }
            return _charLength;
        }

        private int ReadBuffer()
        {
            _charLength = 0;
            _charPosition = 0;
            if (!_checkPreamble)
            {
                _byteLength = 0;
            }

            var eof = false;
            do
            {
                int read;
                if (_checkPreamble)
                {
                    read = _stream.Read(_byteBuffer, _bytePosition, _byteBuffer.Length - _bytePosition);
                    if (read == 0)
                    {
                        eof = true;
                        break;
                    }
                    _byteLength += read;
                }
                else
                {
                    _byteLength = _stream.Read(_byteBuffer, 0, _byteBuffer.Length);
                    if (_byteLength == 0)
                    {
                        eof = true;
                        break;
                    }
                }

                _isBlocked = _byteLength < _byteBuffer.Length;
                if (IsPreamble())
                {
                    continue;
                }
                if (_detectEncoding && _byteLength >= 2)
                {
                    DetectEncoding();
                }

                _charLength = _decoder.GetChars(_byteBuffer, 0, _byteLength, _charBuffer, 0, false);
            }
            while (_charLength == 0);

            if (eof)
            {
                if (_checkPreamble)
                {
                    _checkPreamble = false;
                    _bytePosition = 0;
                }
                _charLength = _decoder.GetChars(_byteBuffer, 0, _byteLength, _charBuffer, 0, true);
                _bytePosition = 0;
                _byteLength = 0;
            }

            return _charLength;
        }

        private void CompressBuffer(int count)
        {
            new ReadOnlySpan<byte>(_byteBuffer, count, _byteLength - count).CopyTo(_byteBuffer);
            _byteLength -= count;
        }

        private void DetectEncoding()
        {
            _detectEncoding = false;
            var changed = false;
            var firstTwoBytes = BinaryPrimitives.ReadUInt16LittleEndian(_byteBuffer);
            if (firstTwoBytes == 0xFFFE)
            {
                _encoding = Encoding.BigEndianUnicode;
                CompressBuffer(2);
                changed = true;
            }
            else if (firstTwoBytes == 0xFEFF)
            {
                if (_byteLength < 4 || _byteBuffer[2] != 0 || _byteBuffer[3] != 0)
                {
                    _encoding = Encoding.Unicode;
                    CompressBuffer(2);
                }
                else
                {
                    _encoding = Encoding.UTF32;
                    CompressBuffer(4);
                }
                changed = true;
            }
            else if (_byteLength >= 3 && firstTwoBytes == 0xBBEF && _byteBuffer[2] == 0xBF)
            {
                _encoding = Encoding.UTF8;
                CompressBuffer(3);
                changed = true;
            }
            else if (_byteLength >= 4 && firstTwoBytes == 0 && _byteBuffer[2] == 0xFE && _byteBuffer[3] == 0xFF)
            {
                _encoding = new UTF32Encoding(true, true);
                CompressBuffer(4);
                changed = true;
            }
            else if (_byteLength == 2)
            {
                _detectEncoding = true;
            }

            if (changed)
            {
                _decoder = _encoding.GetDecoder();
                _maxCharsPerBuffer = _encoding.GetMaxCharCount(_byteBuffer.Length);
                if (_maxCharsPerBuffer > _charBuffer.Length)
                {
                    _charBuffer = new char[_maxCharsPerBuffer];
                }
            }
        }

        private bool IsPreamble()
        {
            if (!_checkPreamble)
            {
                return false;
            }

            var preamble = _encoding.Preamble;
            var length = Math.Min(_byteLength, preamble.Length);
            for (var index = _bytePosition; index < length; index++)
            {
                if (_byteBuffer[index] != preamble[index])
                {
                    _bytePosition = 0;
                    _checkPreamble = false;
                    return false;
                }
            }

            _bytePosition = length;
            if (_bytePosition == preamble.Length)
            {
                CompressBuffer(preamble.Length);
                _bytePosition = 0;
                _checkPreamble = false;
                _detectEncoding = false;
            }

            return _checkPreamble;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null, "Cannot read from a closed reader.");
            }
        }

        internal sealed class NullStreamReader : StreamReader
        {
            internal NullStreamReader() { }
            public override Encoding CurrentEncoding => Encoding.Unicode;
            public override int Peek() => -1;
            public override int Read() => -1;
            public override int Read(char[] buffer, int index, int count) => 0;
            public override Task<int> ReadAsync(char[] buffer, int index, int count) => Task.FromResult(0);
            public override ValueTask<int> ReadAsync(
                Memory<char> buffer,
                CancellationToken cancellationToken = default) =>
                cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled<int>(cancellationToken)
                    : ValueTask.FromResult(0);
            public override int Read(Span<char> buffer) => 0;
            public override int ReadBlock(char[] buffer, int index, int count) => 0;
            public override Task<int> ReadBlockAsync(char[] buffer, int index, int count) => Task.FromResult(0);
            public override ValueTask<int> ReadBlockAsync(
                Memory<char> buffer,
                CancellationToken cancellationToken = default) =>
                cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled<int>(cancellationToken)
                    : ValueTask.FromResult(0);
            public override int ReadBlock(Span<char> buffer) => 0;
            public override string ReadToEnd() => string.Empty;
            public override Task<string> ReadToEndAsync() => Task.FromResult(string.Empty);
            public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled<string>(cancellationToken)
                    : Task.FromResult(string.Empty);
            public override string? ReadLine() => null;
            public override Task<string?> ReadLineAsync() => Task.FromResult<string?>(null);
            public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled<string?>(cancellationToken)
                    : ValueTask.FromResult<string?>(null);
            protected override void Dispose(bool disposing) { }
        }
    }
}
