// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    // This synchronous surface is adapted from dotnet/runtime
    // System.Private.CoreLib (commit 811225a482702af7ecc35d817966bc70b88a3a23).
    // MarshalByRefObject and legacy APM members are intentionally outside the
    // NetWasm stream profile. Task/ValueTask I/O uses the platform scheduler.
    public abstract partial class Stream : IDisposable, IAsyncDisposable
    {
        public static readonly Stream Null = new NullStream();

        public abstract bool CanRead { get; }
        public abstract bool CanWrite { get; }
        public abstract bool CanSeek { get; }
        public virtual bool CanTimeout => false;

        public abstract long Length { get; }
        public abstract long Position { get; set; }

        public virtual int ReadTimeout
        {
            get => throw new InvalidOperationException("Timeouts are not supported on this stream.");
            set => throw new InvalidOperationException("Timeouts are not supported on this stream.");
        }

        public virtual int WriteTimeout
        {
            get => throw new InvalidOperationException("Timeouts are not supported on this stream.");
            set => throw new InvalidOperationException("Timeouts are not supported on this stream.");
        }

        public void CopyTo(Stream destination) => CopyTo(destination, GetCopyBufferSize());

        public Task CopyToAsync(Stream destination) => CopyToAsync(destination, GetCopyBufferSize());

        public Task CopyToAsync(Stream destination, int bufferSize) =>
            CopyToAsync(destination, bufferSize, CancellationToken.None);

        public Task CopyToAsync(Stream destination, CancellationToken cancellationToken) =>
            CopyToAsync(destination, GetCopyBufferSize(), cancellationToken);

        public virtual Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            ValidateCopyToArguments(destination, bufferSize);
            if (!CanRead)
            {
                if (CanWrite)
                {
                    throw new NotSupportedException("Stream does not support reading.");
                }

                throw new ObjectDisposedException(GetType().Name);
            }

            return CopyToAsyncCore(destination, bufferSize, cancellationToken);
        }

        private async Task CopyToAsyncCore(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = await ReadAsync(
                    new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false)) != 0)
                {
                    await destination.WriteAsync(
                        new ReadOnlyMemory<byte>(buffer, 0, bytesRead), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public virtual void CopyTo(Stream destination, int bufferSize)
        {
            ValidateCopyToArguments(destination, bufferSize);
            if (!CanRead)
            {
                if (CanWrite)
                    throw new NotSupportedException("Stream does not support reading.");

                throw new ObjectDisposedException(GetType().Name);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = Read(buffer, 0, buffer.Length)) != 0)
                    destination.Write(buffer, 0, bytesRead);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private int GetCopyBufferSize()
        {
            const int defaultCopyBufferSize = 81920;
            var bufferSize = defaultCopyBufferSize;

            if (CanSeek)
            {
                var length = Length;
                var position = Position;
                if (length <= position)
                    bufferSize = 1;
                else
                {
                    var remaining = length - position;
                    if (remaining > 0)
                        bufferSize = (int)Math.Min(bufferSize, remaining);
                }
            }

            return bufferSize;
        }

        public void Dispose() => Close();

        public virtual void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public virtual ValueTask DisposeAsync()
        {
            try
            {
                Dispose();
                return default;
            }
            catch (Exception exception)
            {
                return ValueTask.FromException(exception);
            }
        }

        public abstract void Flush();

        public Task FlushAsync() => FlushAsync(CancellationToken.None);

        public virtual Task FlushAsync(CancellationToken cancellationToken) =>
            ScheduleAsync(
                static stream =>
                {
                    stream.Flush();
                    return true;
                },
                this,
                cancellationToken);

        public abstract long Seek(long offset, SeekOrigin origin);

        public abstract void SetLength(long value);

        public abstract int Read(byte[] buffer, int offset, int count);

        public Task<int> ReadAsync(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer, offset, count, CancellationToken.None);

        public virtual Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ScheduleAsync(
                static state => state.Stream.Read(state.Buffer, state.Offset, state.Count),
                new ReadWriteOperation(this, buffer, offset, count),
                cancellationToken);
        }

        public virtual ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask<int>(ReadAsync(
                    array.Array!, array.Offset, array.Count, cancellationToken));
            }

            var sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            return FinishReadAsync(
                ReadAsync(sharedBuffer, 0, buffer.Length, cancellationToken),
                sharedBuffer,
                buffer);
        }

        private static async ValueTask<int> FinishReadAsync(
            Task<int> readTask,
            byte[] localBuffer,
            Memory<byte> localDestination)
        {
            try
            {
                var result = await readTask.ConfigureAwait(false);
                new ReadOnlySpan<byte>(localBuffer, 0, result).CopyTo(localDestination.Span);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(localBuffer);
            }
        }

        public virtual int Read(Span<byte> buffer)
        {
            var sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                var bytesRead = Read(sharedBuffer, 0, buffer.Length);
                if ((uint)bytesRead > (uint)buffer.Length)
                    throw new IOException("The stream returned more data than requested.");

                new ReadOnlySpan<byte>(sharedBuffer, 0, bytesRead).CopyTo(buffer);
                return bytesRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }
        }

        public virtual int ReadByte()
        {
            var oneByteArray = new byte[1];
            var bytesRead = Read(oneByteArray, 0, 1);
            return bytesRead == 0 ? -1 : oneByteArray[0];
        }

        public void ReadExactly(Span<byte> buffer) =>
            _ = ReadAtLeastCore(buffer, buffer.Length, throwOnEndOfStream: true);

        public void ReadExactly(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            _ = ReadAtLeastCore(buffer.AsSpan(offset, count), count, throwOnEndOfStream: true);
        }

        public int ReadAtLeast(Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true)
        {
            ValidateReadAtLeastArguments(buffer.Length, minimumBytes);
            return ReadAtLeastCore(buffer, minimumBytes, throwOnEndOfStream);
        }

        public ValueTask ReadExactlyAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            new ValueTask(
                ReadAtLeastAsyncCore(
                    buffer, buffer.Length, throwOnEndOfStream: true, cancellationToken).AsTask());

        public ValueTask ReadExactlyAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            ValidateBufferArguments(buffer, offset, count);
            return new ValueTask(
                ReadAtLeastAsyncCore(
                    buffer.AsMemory(offset, count), count, throwOnEndOfStream: true, cancellationToken)
                    .AsTask());
        }

        public ValueTask<int> ReadAtLeastAsync(
            Memory<byte> buffer,
            int minimumBytes,
            bool throwOnEndOfStream = true,
            CancellationToken cancellationToken = default)
        {
            ValidateReadAtLeastArguments(buffer.Length, minimumBytes);
            return ReadAtLeastAsyncCore(buffer, minimumBytes, throwOnEndOfStream, cancellationToken);
        }

        private async ValueTask<int> ReadAtLeastAsyncCore(
            Memory<byte> buffer,
            int minimumBytes,
            bool throwOnEndOfStream,
            CancellationToken cancellationToken)
        {
            var totalRead = 0;
            while (totalRead < minimumBytes)
            {
                var read = await ReadAsync(buffer.Slice(totalRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    if (throwOnEndOfStream)
                    {
                        throw new EndOfStreamException();
                    }

                    return totalRead;
                }

                totalRead += read;
            }

            return totalRead;
        }

        private int ReadAtLeastCore(Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream)
        {
            Debug.Assert(minimumBytes <= buffer.Length);
            var totalRead = 0;
            while (totalRead < minimumBytes)
            {
                var bytesRead = Read(buffer.Slice(totalRead));
                if (bytesRead == 0)
                {
                    if (throwOnEndOfStream)
                        throw new EndOfStreamException();

                    return totalRead;
                }

                totalRead += bytesRead;
            }

            return totalRead;
        }

        public abstract void Write(byte[] buffer, int offset, int count);

        public Task WriteAsync(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer, offset, count, CancellationToken.None);

        public virtual Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ScheduleAsync(
                static state =>
                {
                    state.Stream.Write(state.Buffer, state.Offset, state.Count);
                    return true;
                },
                new ReadWriteOperation(this, buffer, offset, count),
                cancellationToken);
        }

        public virtual ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask(WriteAsync(
                    array.Array!, array.Offset, array.Count, cancellationToken));
            }

            var sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            buffer.Span.CopyTo(sharedBuffer);
            return new ValueTask(FinishWriteAsync(
                WriteAsync(sharedBuffer, 0, buffer.Length, cancellationToken),
                sharedBuffer));
        }

        private static async Task FinishWriteAsync(Task writeTask, byte[] localBuffer)
        {
            try
            {
                await writeTask.ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(localBuffer);
            }
        }

        public virtual void Write(ReadOnlySpan<byte> buffer)
        {
            var sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(sharedBuffer);
                Write(sharedBuffer, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }
        }

        public virtual void WriteByte(byte value) => Write(new[] { value }, 0, 1);

        public static Stream Synchronized(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return stream is SyncStream ? stream : new SyncStream(stream);
        }

        [Obsolete("Do not call or override this method.")]
        protected virtual void ObjectInvariant()
        {
        }

        protected static void ValidateBufferArguments(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset > buffer.Length - count)
                throw new ArgumentException("Offset and count were out of bounds.");
        }

        private static void ValidateReadAtLeastArguments(int bufferLength, int minimumBytes)
        {
            if (minimumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));
            if (bufferLength < minimumBytes)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));
        }

        private static Task<TResult> ScheduleAsync<TState, TResult>(
            Func<TState, TResult> operation,
            TState state,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<TResult>(cancellationToken);
            }

            var task = new Task<TResult>();
            Runtime.InteropServices.PlatformServices.Scheduler.Schedule(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        task.TrySetCanceled(new TaskCanceledException());
                        return;
                    }

                    try
                    {
                        task.TrySetResult(operation(state));
                    }
                    catch (OperationCanceledException)
                    {
                        task.TrySetCanceled(new TaskCanceledException());
                    }
                    catch (Exception exception)
                    {
                        task.TrySetException(exception);
                    }
                },
                0);
            return task;
        }

        private sealed class ReadWriteOperation
        {
            internal ReadWriteOperation(Stream stream, byte[] buffer, int offset, int count)
            {
                Stream = stream;
                Buffer = buffer;
                Offset = offset;
                Count = count;
            }

            internal Stream Stream { get; }
            internal byte[] Buffer { get; }
            internal int Offset { get; }
            internal int Count { get; }
        }

        protected static void ValidateCopyToArguments(Stream destination, int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            if (!destination.CanWrite)
            {
                if (destination.CanRead)
                    throw new NotSupportedException("Stream does not support writing.");

                throw new ObjectDisposedException(destination.GetType().Name);
            }
        }

        private sealed class NullStream : Stream
        {
            internal NullStream()
            {
            }

            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }

            public override void CopyTo(Stream destination, int bufferSize)
            {
            }

            public override Task CopyToAsync(
                Stream destination,
                int bufferSize,
                CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled(cancellationToken)
                    : Task.CompletedTask;

            protected override void Dispose(bool disposing)
            {
                // The Null singleton cannot be closed.
            }

            public override void Flush()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled(cancellationToken)
                    : Task.CompletedTask;

            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled<int>(cancellationToken)
                    : Task.FromResult(0);
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default) =>
                cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled<int>(cancellationToken)
                    : ValueTask.FromResult(0);
            public override int Read(Span<byte> buffer) => 0;
            public override int ReadByte() => -1;
            public override void Write(byte[] buffer, int offset, int count)
            {
            }
            public override Task WriteAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled(cancellationToken)
                    : Task.CompletedTask;
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default) =>
                cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled(cancellationToken)
                    : ValueTask.CompletedTask;
            public override void Write(ReadOnlySpan<byte> buffer)
            {
            }
            public override void WriteByte(byte value)
            {
            }
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long length)
            {
            }
        }

        private sealed class SyncStream : Stream
        {
            private readonly Stream _stream;

            internal SyncStream(Stream stream) => _stream = stream;

            public override bool CanRead => _stream.CanRead;
            public override bool CanWrite => _stream.CanWrite;
            public override bool CanSeek => _stream.CanSeek;
            public override bool CanTimeout => _stream.CanTimeout;

            public override long Length
            {
                get
                {
                    lock (_stream)
                        return _stream.Length;
                }
            }

            public override long Position
            {
                get
                {
                    lock (_stream)
                        return _stream.Position;
                }
                set
                {
                    lock (_stream)
                        _stream.Position = value;
                }
            }

            public override int ReadTimeout
            {
                get => _stream.ReadTimeout;
                set => _stream.ReadTimeout = value;
            }

            public override int WriteTimeout
            {
                get => _stream.WriteTimeout;
                set => _stream.WriteTimeout = value;
            }

            public override void Close()
            {
                lock (_stream)
                {
                    try
                    {
                        _stream.Close();
                    }
                    finally
                    {
                        base.Dispose(true);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                lock (_stream)
                {
                    if (disposing)
                        _stream.Dispose();
                    base.Dispose(disposing);
                }
            }

            public override void Flush()
            {
                lock (_stream)
                    _stream.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                lock (_stream)
                    return _stream.Read(buffer, offset, count);
            }

            public override int Read(Span<byte> buffer)
            {
                lock (_stream)
                    return _stream.Read(buffer);
            }

            public override int ReadByte()
            {
                lock (_stream)
                    return _stream.ReadByte();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                lock (_stream)
                    return _stream.Seek(offset, origin);
            }

            public override void SetLength(long length)
            {
                lock (_stream)
                    _stream.SetLength(length);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_stream)
                    _stream.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                lock (_stream)
                    _stream.Write(buffer);
            }

            public override void WriteByte(byte value)
            {
                lock (_stream)
                    _stream.WriteByte(value);
            }
        }
    }
}
