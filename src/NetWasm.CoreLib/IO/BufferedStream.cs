// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    // Adapted from dotnet/runtime System.Private.CoreLib commit
    // 474ed963699fc29809015392738a15231aaae2de.
    public sealed class BufferedStream : Stream
    {
        private const int DefaultBufferSize = 4096;

        private Stream? _stream;
        private byte[]? _buffer;
        private readonly int _bufferSize;
        private int _readPos;
        private int _readLen;
        private int _writePos;
        private CachedCompletedInt32Task _lastSyncCompletedReadTask;

        public BufferedStream(Stream stream)
            : this(stream, DefaultBufferSize)
        {
        }

        public BufferedStream(Stream stream, int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            _stream = stream;
            _bufferSize = bufferSize;
            if (!stream.CanRead && !stream.CanWrite)
                throw new ObjectDisposedException(null, "Cannot access a closed stream.");
        }

        private void EnsureNotClosed()
        {
            if (_stream is null)
                throw new ObjectDisposedException(null, "Cannot access a closed stream.");
        }

        private void EnsureCanSeek()
        {
            if (!_stream!.CanSeek)
                throw new NotSupportedException("Stream does not support seeking.");
        }

        private void EnsureCanRead()
        {
            if (!_stream!.CanRead)
                throw new NotSupportedException("Stream does not support reading.");
        }

        private void EnsureCanWrite()
        {
            if (!_stream!.CanWrite)
                throw new NotSupportedException("Stream does not support writing.");
        }

        private void EnsureBufferAllocated() => _buffer ??= new byte[_bufferSize];

        public Stream UnderlyingStream => _stream!;

        public int BufferSize => _bufferSize;

        public override bool CanRead => _stream is not null && _stream.CanRead;
        public override bool CanWrite => _stream is not null && _stream.CanWrite;
        public override bool CanSeek => _stream is not null && _stream.CanSeek;

        public override long Length
        {
            get
            {
                EnsureNotClosed();
                if (_writePos > 0)
                    FlushWrite();
                return _stream!.Length;
            }
        }

        public override long Position
        {
            get
            {
                EnsureNotClosed();
                EnsureCanSeek();
                return _stream!.Position + (_readPos - _readLen + _writePos);
            }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                Seek(value, SeekOrigin.Begin);
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing && _stream is not null)
                {
                    try
                    {
                        Flush();
                    }
                    finally
                    {
                        _stream.Dispose();
                    }
                }
            }
            finally
            {
                _stream = null;
                _buffer = null;
                _writePos = 0;
                _readPos = 0;
                _readLen = 0;
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (_stream is not null)
                {
                    try
                    {
                        await FlushAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await _stream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _stream = null;
                _buffer = null;
                _writePos = 0;
                _readPos = 0;
                _readLen = 0;
            }
        }

        public override void Flush()
        {
            EnsureNotClosed();

            if (_writePos > 0)
            {
                FlushWrite();
                return;
            }

            if (_readPos < _readLen)
            {
                if (_stream!.CanSeek)
                    FlushRead();
                if (_stream.CanWrite)
                    _stream.Flush();
                return;
            }

            if (_stream!.CanWrite)
                _stream.Flush();
            _readPos = _readLen = 0;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            EnsureNotClosed();
            return FlushAsyncInternal(cancellationToken);
        }

        private async Task FlushAsyncInternal(CancellationToken cancellationToken)
        {
            await EnsureAsyncActiveSemaphoreInitialized().WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_writePos > 0)
                {
                    await FlushWriteAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_readPos < _readLen)
                {
                    if (_stream!.CanSeek)
                        FlushRead();
                    if (_stream.CanWrite)
                        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_stream!.CanWrite)
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _asyncActiveSemaphore!.Release();
            }
        }

        private void FlushRead()
        {
            Debug.Assert(_stream is not null);
            Debug.Assert(_writePos == 0);
            if (_readPos != _readLen)
                _stream!.Seek(_readPos - _readLen, SeekOrigin.Current);
            _readPos = 0;
            _readLen = 0;
        }

        private void ClearReadBufferBeforeWrite()
        {
            if (_readPos == _readLen)
            {
                _readPos = _readLen = 0;
                return;
            }

            if (!_stream!.CanSeek)
                throw new NotSupportedException("Cannot write to a buffered stream when its read buffer cannot be flushed.");
            FlushRead();
        }

        private void FlushWrite()
        {
            Debug.Assert(_stream is not null);
            Debug.Assert(_readPos == 0 && _readLen == 0);
            Debug.Assert(_buffer is not null && _writePos <= _bufferSize);
            _stream!.Write(_buffer!, 0, _writePos);
            _writePos = 0;
            _stream.Flush();
        }

        private async ValueTask FlushWriteAsync(CancellationToken cancellationToken)
        {
            await _stream!.WriteAsync(
                new ReadOnlyMemory<byte>(_buffer!, 0, _writePos),
                cancellationToken).ConfigureAwait(false);
            _writePos = 0;
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private int ReadFromBuffer(byte[] destination, int offset, int count)
        {
            var bytesToRead = Math.Min(_readLen - _readPos, count);
            if (bytesToRead > 0)
            {
                Buffer.BlockCopy(_buffer!, _readPos, destination, offset, bytesToRead);
                _readPos += bytesToRead;
            }
            return bytesToRead;
        }

        private int ReadFromBuffer(Span<byte> destination)
        {
            var bytesToRead = Math.Min(_readLen - _readPos, destination.Length);
            if (bytesToRead > 0)
            {
                new ReadOnlySpan<byte>(_buffer!, _readPos, bytesToRead).CopyTo(destination);
                _readPos += bytesToRead;
            }
            return bytesToRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            EnsureNotClosed();
            EnsureCanRead();

            if (count == 0)
                return 0;

            var bytesFromBuffer = ReadFromBuffer(buffer, offset, count);
            if (bytesFromBuffer == count)
                return bytesFromBuffer;

            if (bytesFromBuffer > 0)
            {
                FlushRead();
                if (_writePos > 0)
                    FlushWrite();
                return bytesFromBuffer + _stream!.Read(buffer, offset + bytesFromBuffer, count - bytesFromBuffer);
            }

            if (_writePos > 0)
                FlushWrite();

            if (count >= _bufferSize)
                return _stream!.Read(buffer, offset, count);

            EnsureBufferAllocated();
            _readLen = _stream!.Read(_buffer!, 0, _bufferSize);
            _readPos = 0;
            return ReadFromBuffer(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            EnsureNotClosed();
            EnsureCanRead();

            if (buffer.Length == 0)
                return 0;

            var bytesFromBuffer = ReadFromBuffer(buffer);
            if (bytesFromBuffer == buffer.Length)
                return bytesFromBuffer;

            if (bytesFromBuffer > 0)
            {
                FlushRead();
                if (_writePos > 0)
                    FlushWrite();
                return bytesFromBuffer + _stream!.Read(buffer.Slice(bytesFromBuffer));
            }

            if (_writePos > 0)
                FlushWrite();

            if (buffer.Length >= _bufferSize)
                return _stream!.Read(buffer);

            EnsureBufferAllocated();
            _readLen = _stream!.Read(_buffer!, 0, _bufferSize);
            _readPos = 0;
            return ReadFromBuffer(buffer);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);
            EnsureNotClosed();
            EnsureCanRead();

            var bytesFromBuffer = 0;
            var semaphore = EnsureAsyncActiveSemaphoreInitialized();
            var semaphoreTask = semaphore.WaitAsync(cancellationToken);
            if (semaphoreTask.IsCompletedSuccessfully)
            {
                var completeSynchronously = true;
                try
                {
                    bytesFromBuffer = ReadFromBuffer(buffer, offset, count);
                    completeSynchronously = bytesFromBuffer == count;
                    if (completeSynchronously)
                        return _lastSyncCompletedReadTask.GetTask(bytesFromBuffer);
                }
                finally
                {
                    if (completeSynchronously)
                        semaphore.Release();
                }
            }

            return ReadFromUnderlyingStreamAsync(
                new Memory<byte>(buffer, offset + bytesFromBuffer, count - bytesFromBuffer),
                cancellationToken,
                bytesFromBuffer,
                semaphoreTask).AsTask();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);
            EnsureNotClosed();
            EnsureCanRead();

            var bytesFromBuffer = 0;
            var semaphore = EnsureAsyncActiveSemaphoreInitialized();
            var semaphoreTask = semaphore.WaitAsync(cancellationToken);
            if (semaphoreTask.IsCompletedSuccessfully)
            {
                var completeSynchronously = true;
                try
                {
                    bytesFromBuffer = ReadFromBuffer(buffer.Span);
                    completeSynchronously = bytesFromBuffer == buffer.Length;
                    if (completeSynchronously)
                        return new ValueTask<int>(bytesFromBuffer);
                }
                finally
                {
                    if (completeSynchronously)
                        semaphore.Release();
                }
            }

            return ReadFromUnderlyingStreamAsync(
                buffer.Slice(bytesFromBuffer),
                cancellationToken,
                bytesFromBuffer,
                semaphoreTask);
        }

        private async ValueTask<int> ReadFromUnderlyingStreamAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken,
            int bytesAlreadySatisfied,
            Task semaphoreTask)
        {
            await semaphoreTask.ConfigureAwait(false);
            try
            {
                var bytesFromBuffer = ReadFromBuffer(buffer.Span);
                if (bytesFromBuffer == buffer.Length)
                    return bytesAlreadySatisfied + bytesFromBuffer;
                if (bytesFromBuffer > 0)
                {
                    buffer = buffer.Slice(bytesFromBuffer);
                    bytesAlreadySatisfied += bytesFromBuffer;
                }

                _readPos = _readLen = 0;
                if (_writePos > 0)
                    await FlushWriteAsync(cancellationToken).ConfigureAwait(false);

                if (buffer.Length >= _bufferSize)
                {
                    return bytesAlreadySatisfied +
                        await _stream!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }

                EnsureBufferAllocated();
                _readLen = await _stream!.ReadAsync(
                    new Memory<byte>(_buffer!, 0, _bufferSize),
                    cancellationToken).ConfigureAwait(false);
                return bytesAlreadySatisfied + ReadFromBuffer(buffer.Span);
            }
            finally
            {
                _asyncActiveSemaphore!.Release();
            }
        }

        public override int ReadByte()
        {
            if (_readPos < _readLen)
                return _buffer![_readPos++];

            EnsureNotClosed();
            EnsureCanRead();
            EnsureBufferAllocated();
            if (_writePos > 0)
                FlushWrite();
            _readLen = _stream!.Read(_buffer!, 0, _bufferSize);
            _readPos = 0;
            return _readLen == 0 ? -1 : _buffer![_readPos++];
        }

        private void WriteToBuffer(byte[] source, ref int offset, ref int count)
        {
            EnsureBufferAllocated();
            var bytesToWrite = Math.Min(_bufferSize - _writePos, count);
            if (bytesToWrite > 0)
            {
                Buffer.BlockCopy(source, offset, _buffer!, _writePos, bytesToWrite);
                _writePos += bytesToWrite;
                offset += bytesToWrite;
                count -= bytesToWrite;
            }
        }

        private int WriteToBuffer(ReadOnlySpan<byte> source)
        {
            EnsureBufferAllocated();
            var bytesToWrite = Math.Min(_bufferSize - _writePos, source.Length);
            if (bytesToWrite > 0)
            {
                source.Slice(0, bytesToWrite).CopyTo(new Span<byte>(_buffer!, _writePos, bytesToWrite));
                _writePos += bytesToWrite;
            }
            return bytesToWrite;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            EnsureNotClosed();
            EnsureCanWrite();
            if (_writePos == 0)
                ClearReadBufferBeforeWrite();

            if (count >= _bufferSize)
            {
                if (_writePos > 0)
                    FlushWrite();
                _stream!.Write(buffer, offset, count);
                return;
            }

            while (count > 0)
            {
                if (_writePos == _bufferSize)
                {
                    _stream!.Write(_buffer!, 0, _writePos);
                    _writePos = 0;
                }

                WriteToBuffer(buffer, ref offset, ref count);
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureNotClosed();
            EnsureCanWrite();
            if (_writePos == 0)
                ClearReadBufferBeforeWrite();

            if (buffer.Length >= _bufferSize)
            {
                if (_writePos > 0)
                    FlushWrite();
                _stream!.Write(buffer);
                return;
            }

            while (!buffer.IsEmpty)
            {
                if (_writePos == _bufferSize)
                {
                    _stream!.Write(_buffer!, 0, _writePos);
                    _writePos = 0;
                }

                var bytesWritten = WriteToBuffer(buffer);
                buffer = buffer.Slice(bytesWritten);
            }
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return WriteAsync(
                new ReadOnlyMemory<byte>(buffer, offset, count),
                cancellationToken).AsTask();
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);
            EnsureNotClosed();
            EnsureCanWrite();

            var semaphore = EnsureAsyncActiveSemaphoreInitialized();
            var semaphoreTask = semaphore.WaitAsync(cancellationToken);
            if (semaphoreTask.IsCompletedSuccessfully)
            {
                var completeSynchronously = true;
                try
                {
                    if (_writePos == 0)
                        ClearReadBufferBeforeWrite();
                    completeSynchronously = buffer.Length < _bufferSize - _writePos;
                    if (completeSynchronously)
                    {
                        WriteToBuffer(buffer.Span);
                        return default;
                    }
                }
                finally
                {
                    if (completeSynchronously)
                        semaphore.Release();
                }
            }

            return WriteToUnderlyingStreamAsync(buffer, cancellationToken, semaphoreTask);
        }

        private async ValueTask WriteToUnderlyingStreamAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken,
            Task semaphoreTask)
        {
            await semaphoreTask.ConfigureAwait(false);
            try
            {
                if (_writePos == 0)
                    ClearReadBufferBeforeWrite();

                if (buffer.Length >= _bufferSize)
                {
                    if (_writePos > 0)
                    {
                        await _stream!.WriteAsync(
                            new ReadOnlyMemory<byte>(_buffer!, 0, _writePos),
                            cancellationToken).ConfigureAwait(false);
                        _writePos = 0;
                    }
                    await _stream!.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    return;
                }

                while (!buffer.IsEmpty)
                {
                    if (_writePos == _bufferSize)
                    {
                        await _stream!.WriteAsync(
                            new ReadOnlyMemory<byte>(_buffer!, 0, _writePos),
                            cancellationToken).ConfigureAwait(false);
                        _writePos = 0;
                    }
                    buffer = buffer.Slice(WriteToBuffer(buffer.Span));
                }
            }
            finally
            {
                _asyncActiveSemaphore!.Release();
            }
        }

        public override void WriteByte(byte value)
        {
            if (_writePos > 0 && _writePos < _bufferSize)
            {
                _buffer![_writePos++] = value;
                return;
            }

            EnsureNotClosed();
            EnsureCanWrite();
            if (_writePos == 0)
                ClearReadBufferBeforeWrite();
            EnsureBufferAllocated();
            if (_writePos == _bufferSize)
            {
                _stream!.Write(_buffer!, 0, _writePos);
                _writePos = 0;
            }
            _buffer![_writePos++] = value;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureNotClosed();
            EnsureCanSeek();

            if (_writePos > 0)
            {
                FlushWrite();
                return _stream!.Seek(offset, origin);
            }

            if (_readLen - _readPos > 0 && origin == SeekOrigin.Current)
                offset -= _readLen - _readPos;

            var oldPosition = Position;
            var newPosition = _stream!.Seek(offset, origin);
            var readPosition = newPosition - (oldPosition - _readPos);
            if (readPosition >= 0 && readPosition < _readLen)
            {
                _readPos = (int)readPosition;
                _stream.Seek(_readLen - _readPos, SeekOrigin.Current);
            }
            else
            {
                _readPos = _readLen = 0;
            }

            return newPosition;
        }

        public override void SetLength(long value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            EnsureNotClosed();
            EnsureCanSeek();
            EnsureCanWrite();
            Flush();
            _stream!.SetLength(value);
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            ValidateCopyToArguments(destination, bufferSize);
            EnsureNotClosed();
            EnsureCanRead();

            var readBytes = _readLen - _readPos;
            if (readBytes > 0)
            {
                destination.Write(_buffer!, _readPos, readBytes);
                _readPos = _readLen = 0;
            }
            else if (_writePos > 0)
            {
                FlushWrite();
            }

            _stream!.CopyTo(destination, bufferSize);
        }

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            ValidateCopyToArguments(destination, bufferSize);
            EnsureNotClosed();
            EnsureCanRead();
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : CopyToAsyncCore(destination, bufferSize, cancellationToken);
        }

        private async Task CopyToAsyncCore(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            await EnsureAsyncActiveSemaphoreInitialized().WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var readBytes = _readLen - _readPos;
                if (readBytes > 0)
                {
                    await destination.WriteAsync(
                        new ReadOnlyMemory<byte>(_buffer!, _readPos, readBytes),
                        cancellationToken).ConfigureAwait(false);
                    _readPos = _readLen = 0;
                }
                else if (_writePos > 0)
                {
                    await FlushWriteAsync(cancellationToken).ConfigureAwait(false);
                }

                await _stream!.CopyToAsync(
                    destination,
                    bufferSize,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _asyncActiveSemaphore!.Release();
            }
        }
    }
}
