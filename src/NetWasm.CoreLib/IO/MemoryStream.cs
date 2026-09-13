// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    // A MemoryStream represents a Stream in memory. This implementation is
    // adapted from dotnet/runtime System.Private.CoreLib commit
    // 811225a482702af7ecc35d817966bc70b88a3a23.
    public class MemoryStream : Stream
    {
        private byte[] _buffer;
        private readonly int _origin;
        private int _position;
        private int _length;
        private int _capacity;
        private bool _expandable;
        private bool _writable;
        private readonly bool _exposable;
        private bool _isOpen;
        private Task<int>? _lastReadTask;

        public MemoryStream()
            : this(0)
        {
        }

        public MemoryStream(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(capacity);

            _buffer = capacity == 0 ? Array.Empty<byte>() : new byte[capacity];
            _capacity = capacity;
            _expandable = true;
            _writable = true;
            _exposable = true;
            _isOpen = true;
        }

        public MemoryStream(byte[] buffer)
            : this(buffer, true)
        {
        }

        public MemoryStream(byte[] buffer, bool writable)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            _buffer = buffer;
            _length = _capacity = buffer.Length;
            _writable = writable;
            _isOpen = true;
        }

        public MemoryStream(byte[] buffer, int index, int count)
            : this(buffer, index, count, true, false)
        {
        }

        public MemoryStream(byte[] buffer, int index, int count, bool writable)
            : this(buffer, index, count, writable, false)
        {
        }

        public MemoryStream(byte[] buffer, int index, int count, bool writable, bool publiclyVisible)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (index > buffer.Length - count)
                throw new ArgumentException("Offset and count were out of bounds.");

            _buffer = buffer;
            _origin = _position = index;
            _length = _capacity = index + count;
            _writable = writable;
            _exposable = publiclyVisible;
            _isOpen = true;
        }

        public override bool CanRead => _isOpen;
        public override bool CanSeek => _isOpen;
        public override bool CanWrite => _writable;

        private void EnsureNotClosed()
        {
            if (!_isOpen)
                throw new ObjectDisposedException(null, "Cannot access a closed stream.");
        }

        private void EnsureWriteable()
        {
            if (!CanWrite)
                throw new NotSupportedException("Stream does not support writing.");
        }

        private static Task CreateCanceledTask(OperationCanceledException exception)
        {
            var task = new Task();
            task.SetCanceled(exception);
            return task;
        }

        private static Task<int> CreateCanceledReadTask(OperationCanceledException exception)
        {
            var task = new Task<int>();
            task.SetCanceled(exception);
            return task;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _isOpen = false;
                _writable = false;
                _expandable = false;
                _lastReadTask = null;
            }
        }

        private bool EnsureCapacity(int value)
        {
            if (value < 0)
                throw new IOException("Stream was too long.");

            if (value > _capacity)
            {
                var newCapacity = Math.Max(value, 256);
                if (newCapacity < _capacity * 2)
                    newCapacity = _capacity * 2;
                if ((uint)(_capacity * 2) > Array.MaxLength)
                    newCapacity = Math.Max(value, Array.MaxLength);

                Capacity = newCapacity;
                return true;
            }

            return false;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            try
            {
                Flush();
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        public virtual byte[] GetBuffer()
        {
            if (!_exposable)
                throw new UnauthorizedAccessException("The underlying buffer is not publicly visible.");
            return _buffer;
        }

        public virtual bool TryGetBuffer(out ArraySegment<byte> buffer)
        {
            if (!_exposable)
            {
                buffer = default;
                return false;
            }

            buffer = new ArraySegment<byte>(_buffer, _origin, _length - _origin);
            return true;
        }

        internal int InternalEmulateRead(int count)
        {
            EnsureNotClosed();

            var bytesAvailable = _length - _position;
            if (bytesAvailable > count)
                bytesAvailable = count;
            if (bytesAvailable < 0)
                bytesAvailable = 0;

            Debug.Assert(_position + bytesAvailable >= 0);
            _position += bytesAvailable;
            return bytesAvailable;
        }

        public virtual int Capacity
        {
            get
            {
                EnsureNotClosed();
                return _capacity - _origin;
            }
            set
            {
                if (value < Length)
                    throw new ArgumentOutOfRangeException(nameof(value));

                EnsureNotClosed();
                if (!_expandable && value != Capacity)
                    throw new NotSupportedException("Memory stream is not expandable.");

                if (_expandable && value != _capacity)
                {
                    _buffer = value == 0 ? Array.Empty<byte>() : CopyToNewBuffer(value);
                    _capacity = value;
                }
            }
        }

        private byte[] CopyToNewBuffer(int capacity)
        {
            var newBuffer = new byte[capacity];
            if (_length > 0)
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
            return newBuffer;
        }

        public override long Length
        {
            get
            {
                EnsureNotClosed();
                return _length - _origin;
            }
        }

        public override long Position
        {
            get
            {
                EnsureNotClosed();
                return _position - _origin;
            }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                EnsureNotClosed();
                if (value > int.MaxValue - _origin)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = _origin + (int)value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            EnsureNotClosed();

            var bytesAvailable = _length - _position;
            if (bytesAvailable > count)
                bytesAvailable = count;
            if (bytesAvailable <= 0)
                return 0;

            Debug.Assert(_position + bytesAvailable >= 0);
            if (bytesAvailable <= 8)
            {
                var byteCount = bytesAvailable;
                while (--byteCount >= 0)
                    buffer[offset + byteCount] = _buffer[_position + byteCount];
            }
            else
            {
                Buffer.BlockCopy(_buffer, _position, buffer, offset, bytesAvailable);
            }

            _position += bytesAvailable;
            return bytesAvailable;
        }

        public override int Read(Span<byte> buffer)
        {
            if (GetType() != typeof(MemoryStream))
                return base.Read(buffer);

            EnsureNotClosed();
            var bytesAvailable = Math.Min(_length - _position, buffer.Length);
            if (bytesAvailable <= 0)
                return 0;

            new Span<byte>(_buffer, _position, bytesAvailable).CopyTo(buffer);
            _position += bytesAvailable;
            return bytesAvailable;
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

            try
            {
                var bytesRead = Read(buffer, offset, count);
                if (_lastReadTask is Task<int> task &&
                    task.IsCompletedSuccessfully &&
                    task.Result == bytesRead)
                {
                    return task;
                }

                return _lastReadTask = Task.FromResult(bytesRead);
            }
            catch (OperationCanceledException exception)
            {
                return CreateCanceledReadTask(exception);
            }
            catch (Exception exception)
            {
                return Task.FromException<int>(exception);
            }
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);

            try
            {
                return new ValueTask<int>(
                    MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> destinationArray)
                        ? Read(destinationArray.Array!, destinationArray.Offset, destinationArray.Count)
                        : Read(buffer.Span));
            }
            catch (OperationCanceledException exception)
            {
                return new ValueTask<int>(CreateCanceledReadTask(exception));
            }
            catch (Exception exception)
            {
                return ValueTask.FromException<int>(exception);
            }
        }

        public override int ReadByte()
        {
            EnsureNotClosed();
            if (_position >= _length)
                return -1;
            return _buffer[_position++];
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            if (GetType() != typeof(MemoryStream))
            {
                base.CopyTo(destination, bufferSize);
                return;
            }

            ValidateCopyToArguments(destination, bufferSize);
            EnsureNotClosed();
            var originalPosition = _position;
            var remaining = InternalEmulateRead(_length - originalPosition);
            if (remaining > 0)
                destination.Write(_buffer, originalPosition, remaining);
        }

        public override Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            ValidateCopyToArguments(destination, bufferSize);
            EnsureNotClosed();

            if (GetType() != typeof(MemoryStream))
                return base.CopyToAsync(destination, bufferSize, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            var position = _position;
            var bytesRemaining = InternalEmulateRead(_length - _position);
            if (bytesRemaining == 0)
                return Task.CompletedTask;

            if (destination is not MemoryStream destinationMemoryStream)
                return destination.WriteAsync(_buffer, position, bytesRemaining, cancellationToken);

            try
            {
                destinationMemoryStream.Write(_buffer, position, bytesRemaining);
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureNotClosed();
            var location = origin switch
            {
                SeekOrigin.Begin => _origin,
                SeekOrigin.Current => _position,
                SeekOrigin.End => _length,
                _ => throw new ArgumentException("Invalid seek origin.")
            };
            return SeekCore(offset, location);
        }

        private long SeekCore(long offset, int location)
        {
            if (offset > int.MaxValue - location)
                throw new ArgumentOutOfRangeException(nameof(offset));

            var newPosition = unchecked(location + (int)offset);
            if (unchecked(location + offset) < _origin || newPosition < _origin)
                throw new IOException("An attempt was made to move the position before the beginning of the stream.");

            _position = newPosition;
            Debug.Assert(_position >= _origin);
            return _position - _origin;
        }

        public override void SetLength(long value)
        {
            if (value < 0 || value > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));

            EnsureWriteable();
            if (value > int.MaxValue - _origin)
                throw new ArgumentOutOfRangeException(nameof(value));

            var newLength = _origin + (int)value;
            var allocatedNewArray = EnsureCapacity(newLength);
            if (!allocatedNewArray && newLength > _length)
                Array.Clear(_buffer, _length, newLength - _length);
            _length = newLength;
            if (_position > newLength)
                _position = newLength;
        }

        public virtual byte[] ToArray()
        {
            var count = _length - _origin;
            if (count == 0)
                return Array.Empty<byte>();

            var copy = new byte[count];
            new ReadOnlySpan<byte>(_buffer, _origin, count).CopyTo(copy);
            return copy;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            EnsureNotClosed();
            EnsureWriteable();

            var endPosition = _position + count;
            if (endPosition < 0)
                throw new IOException("Stream was too long.");

            if (endPosition > _length)
            {
                var mustZero = _position > _length;
                if (endPosition > _capacity)
                {
                    var allocatedNewArray = EnsureCapacity(endPosition);
                    if (allocatedNewArray)
                        mustZero = false;
                }
                if (mustZero)
                    Array.Clear(_buffer, _length, endPosition - _length);
                _length = endPosition;
            }

            if (count <= 8 && !ReferenceEquals(buffer, _buffer))
            {
                var byteCount = count;
                while (--byteCount >= 0)
                    _buffer[_position + byteCount] = buffer[offset + byteCount];
            }
            else
            {
                Buffer.BlockCopy(buffer, offset, _buffer, _position, count);
            }

            _position = endPosition;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (GetType() != typeof(MemoryStream))
            {
                base.Write(buffer);
                return;
            }

            EnsureNotClosed();
            EnsureWriteable();
            var endPosition = _position + buffer.Length;
            if (endPosition < 0)
                throw new IOException("Stream was too long.");

            if (endPosition > _length)
            {
                var mustZero = _position > _length;
                if (endPosition > _capacity)
                {
                    var allocatedNewArray = EnsureCapacity(endPosition);
                    if (allocatedNewArray)
                        mustZero = false;
                }
                if (mustZero)
                    Array.Clear(_buffer, _length, endPosition - _length);
                _length = endPosition;
            }

            buffer.CopyTo(new Span<byte>(_buffer, _position, buffer.Length));
            _position = endPosition;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            try
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            catch (OperationCanceledException exception)
            {
                return CreateCanceledTask(exception);
            }
            catch (Exception exception)
            {
                return Task.FromException(exception);
            }
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);

            try
            {
                if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> sourceArray))
                    Write(sourceArray.Array!, sourceArray.Offset, sourceArray.Count);
                else
                    Write(buffer.Span);

                return default;
            }
            catch (OperationCanceledException exception)
            {
                return new ValueTask(CreateCanceledTask(exception));
            }
            catch (Exception exception)
            {
                return ValueTask.FromException(exception);
            }
        }

        public override void WriteByte(byte value)
        {
            EnsureNotClosed();
            EnsureWriteable();

            if (_position >= _length)
            {
                var newLength = _position + 1;
                var mustZero = _position > _length;
                if (newLength >= _capacity)
                {
                    var allocatedNewArray = EnsureCapacity(newLength);
                    if (allocatedNewArray)
                        mustZero = false;
                }
                if (mustZero)
                    Array.Clear(_buffer, _length, _position - _length);
                _length = newLength;
            }

            _buffer[_position++] = value;
        }

        public virtual void WriteTo(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            EnsureNotClosed();
            stream.Write(_buffer, _origin, _length - _origin);
        }
    }
}
