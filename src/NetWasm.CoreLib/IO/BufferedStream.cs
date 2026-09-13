// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace System.IO
{
    // This synchronous buffering implementation is adapted from dotnet/runtime
    // System.Private.CoreLib commit 811225a482702af7ecc35d817966bc70b88a3a23.
    // Async members and their synchronization machinery belong to a later
    // profile slice.
    public sealed class BufferedStream : Stream
    {
        private const int DefaultBufferSize = 4096;

        private Stream? _stream;
        private byte[]? _buffer;
        private readonly int _bufferSize;
        private int _readPos;
        private int _readLen;
        private int _writePos;

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
    }
}
