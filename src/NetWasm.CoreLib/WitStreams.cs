namespace System.Runtime.InteropServices.WebAssembly
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    // The built-ins of one stream type, which generated bindings implement over the canonical
    // stream.new, stream.read, stream.write and drop built-ins of a function that uses the type.
    // Built-ins of structurally equal stream types are interchangeable.
    public abstract class WitStreamIntrinsics
    {
        // The new stream's readable end, and its writable end shifted left 32 bits.
        public abstract long New();

        // An async-lowered copy: BLOCKED (-1) or count << 4 | (0 completed, 1 dropped, 2 cancelled).
        public abstract int Read(int readable, nuint buffer, nuint count);

        public abstract int Write(int writable, nuint buffer, nuint count);

        public abstract void DropReadable(int readable);

        public abstract void DropWritable(int writable);
    }

    // The built-ins of one future type, as WitStreamIntrinsics are a stream type's.
    public abstract class WitFutureIntrinsics
    {
        public abstract long New();

        public abstract int Read(int readable, nuint buffer);

        public abstract int Write(int writable, nuint buffer);

        public abstract void DropReadable(int readable);

        public abstract void DropWritable(int writable);
    }

    // A received stream<u8>: each read copies from the stream's readable end, and the writer
    // dropping its end is the end of the data. A synchronous read waits for the copy, which only
    // an async-typed task may do. Disposing the reader drops its end.
    public sealed class WitStreamReader : Stream
    {
        private const int Blocked = -1;
        private const uint Dropped = 1;

        private readonly WitStreamIntrinsics _intrinsics;
        private int _readable;
        private bool _ended;

        public WitStreamReader(int readable, WitStreamIntrinsics intrinsics)
        {
            if (readable == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(readable));
            }

            _readable = readable;
            _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
        }

        public override bool CanRead => _readable != 0;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            if (!Begin(count, out var native))
            {
                return 0;
            }

            try
            {
                var status = _intrinsics.Read(_readable, native, unchecked((nuint)count));
                var result = status == Blocked
                    ? WasiPlatformServicesComposition.CreateWaitableEventAwaiter().Await(_readable)
                    : unchecked((uint)status);
                return Finish(result, native, buffer.AsSpan(offset, count));
            }
            finally
            {
                CanonicalAbi.Free(native);
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Begin(buffer.Length, out var native))
            {
                return 0;
            }

            try
            {
                var status = _intrinsics.Read(_readable, native, unchecked((nuint)buffer.Length));
                var result = await CanonicalAbi.AwaitCopy(_readable, status).ConfigureAwait(false);
                return Finish(unchecked((uint)result), native, buffer.Span);
            }
            finally
            {
                CanonicalAbi.Free(native);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_readable != 0)
            {
                var readable = _readable;
                _readable = 0;
                _intrinsics.DropReadable(readable);
            }

            base.Dispose(disposing);
        }

        // False when nothing can be read: the stream ended, or the buffer is empty.
        private bool Begin(int count, out nuint native)
        {
            if (_readable == 0)
            {
                throw new ObjectDisposedException(nameof(WitStreamReader));
            }

            native = 0;
            if (_ended || count == 0)
            {
                return false;
            }

            native = CanonicalAbi.Allocate(unchecked((nuint)count), 1);
            return true;
        }

        private unsafe int Finish(uint result, nuint native, Span<byte> destination)
        {
            var copied = unchecked((int)(result >> 4));
            new ReadOnlySpan<byte>((void*)native, copied).CopyTo(destination);
            if ((result & 0xF) == Dropped)
            {
                _ended = true;
            }

            return copied;
        }
    }

    // A sent stream<u8>: the bytes of a source Stream copied into a new stream's writable end,
    // whose readable end the callee receives. The copy runs on the event loop; it ends, and
    // drops its end, at the source's end or when the reader drops the stream.
    internal static class WitStreamWriter
    {
        private const int BufferSize = 4096;
        private const uint Dropped = 1;

        internal static int Start(Stream source, WitStreamIntrinsics intrinsics)
        {
            if (source == null || intrinsics == null)
            {
                throw new ArgumentNullException();
            }

            var ends = intrinsics.New();
            var writable = unchecked((int)(ends >> 32));
            if (PlatformServices.CurrentSynchronousLowering is { } synchronous)
            {
                synchronous.Defer(() => _ = Copy(source, writable, intrinsics));
            }
            else
            {
                _ = Copy(source, writable, intrinsics);
            }

            return unchecked((int)ends);
        }

        private static async Task Copy(Stream source, int writable, WitStreamIntrinsics intrinsics)
        {
            var managed = new byte[BufferSize];
            var native = CanonicalAbi.Allocate(BufferSize, 1);
            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(managed, 0, BufferSize).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    unsafe
                    {
                        managed.AsSpan(0, read).CopyTo(new Span<byte>((void*)native, read));
                    }

                    var written = 0;
                    while (written < read)
                    {
                        var status = intrinsics.Write(
                            writable,
                            native + unchecked((nuint)written),
                            unchecked((nuint)(read - written)));
                        var result = unchecked((uint)await CanonicalAbi.AwaitCopy(writable, status).ConfigureAwait(false));
                        written += unchecked((int)(result >> 4));
                        if ((result & 0xF) == Dropped)
                        {
                            return;
                        }
                    }
                }
            }
            finally
            {
                CanonicalAbi.Free(native);
                intrinsics.DropWritable(writable);
            }
        }
    }

    // A stream of elements other than bytes: a received one is an IAsyncEnumerable<T> read a
    // chunk at a time, each element lifted from its canonical layout (lifting frees what the
    // element owns); a sent one copies an IAsyncEnumerable<T> into a new stream, an element at a
    // time, releasing what lowering each element allocated once the reader has copied it.
    internal static class WitElementStreams
    {
        private const int ChunkElements = 64;
        private const uint Completed = 0;

        internal static async IAsyncEnumerable<T> Read<T>(
            int readable,
            WitStreamIntrinsics intrinsics,
            nuint size,
            nuint alignment,
            Func<nuint, T> lift)
        {
            var stride = size == 0 ? 1 : size;
            var buffer = CanonicalAbi.Allocate(stride * ChunkElements, alignment);
            try
            {
                while (true)
                {
                    var status = intrinsics.Read(readable, buffer, ChunkElements);
                    var result = unchecked((uint)await CanonicalAbi.AwaitCopy(readable, status).ConfigureAwait(false));
                    var count = unchecked((nuint)(result >> 4));
                    for (nuint index = 0; index < count; index++)
                    {
                        yield return lift(buffer + index * size);
                    }

                    if ((result & 0xF) != Completed)
                    {
                        yield break;
                    }
                }
            }
            finally
            {
                CanonicalAbi.Free(buffer);
                intrinsics.DropReadable(readable);
            }
        }

        internal static int Start<T>(
            IAsyncEnumerable<T> source,
            WitStreamIntrinsics intrinsics,
            nuint size,
            nuint alignment,
            Action<T, nuint> lower,
            Action<nuint> release)
        {
            if (source == null || intrinsics == null || lower == null || release == null)
            {
                throw new ArgumentNullException();
            }

            var ends = intrinsics.New();
            var writable = unchecked((int)(ends >> 32));
            if (PlatformServices.CurrentSynchronousLowering is { } synchronous)
            {
                synchronous.Defer(() => _ = Copy(source, writable, intrinsics, size, alignment, lower, release));
            }
            else
            {
                _ = Copy(source, writable, intrinsics, size, alignment, lower, release);
            }

            return unchecked((int)ends);
        }

        private static async Task Copy<T>(
            IAsyncEnumerable<T> source,
            int writable,
            WitStreamIntrinsics intrinsics,
            nuint size,
            nuint alignment,
            Action<T, nuint> lower,
            Action<nuint> release)
        {
            var buffer = CanonicalAbi.Allocate(size == 0 ? 1 : size, alignment);
            try
            {
                await foreach (var element in source.ConfigureAwait(false))
                {
                    lower(element, buffer);
                    uint result;
                    try
                    {
                        var status = intrinsics.Write(writable, buffer, 1);
                        result = unchecked((uint)await CanonicalAbi.AwaitCopy(writable, status).ConfigureAwait(false));
                    }
                    finally
                    {
                        release(buffer);
                    }

                    if ((result & 0xF) != Completed)
                    {
                        return;
                    }
                }
            }
            finally
            {
                CanonicalAbi.Free(buffer);
                intrinsics.DropWritable(writable);
            }
        }
    }

    // A received future<T> as a Task<T>, and a sent Task<T> as a future<T> written when it
    // completes. The payload is lifted from, or lowered into, a buffer of its canonical size.
    internal static class WitFutures
    {
        private const uint Completed = 0;

        internal static async Task<T> Read<T>(
            int readable,
            WitFutureIntrinsics intrinsics,
            nuint size,
            nuint alignment,
            Func<nuint, T> lift)
        {
            var buffer = CanonicalAbi.Allocate(size == 0 ? 1 : size, alignment);
            try
            {
                // A future completes on the event loop even in a synchronous call, which reads futures it is handed
                // eagerly and may never await them.
                var status = intrinsics.Read(readable, buffer);
                var result = unchecked((uint)await PlatformServices.AwaitCopyOnEventLoop(readable, status).ConfigureAwait(false));
                if ((result & 0xF) != Completed)
                {
                    throw new IOException("The future's writer dropped it without a value.");
                }

                return lift(buffer);
            }
            finally
            {
                CanonicalAbi.Free(buffer);
                intrinsics.DropReadable(readable);
            }
        }

        // Release frees what lowering the value allocated, once the reader has copied it.
        internal static int Start<T>(
            Task<T> value,
            WitFutureIntrinsics intrinsics,
            nuint size,
            nuint alignment,
            Action<T, nuint> lower,
            Action<nuint> release)
        {
            if (value == null || intrinsics == null || lower == null || release == null)
            {
                throw new ArgumentNullException();
            }

            var ends = intrinsics.New();
            _ = Write(value, unchecked((int)(ends >> 32)), intrinsics, size, alignment, lower, release);
            return unchecked((int)ends);
        }

        private static async Task Write<T>(
            Task<T> value,
            int writable,
            WitFutureIntrinsics intrinsics,
            nuint size,
            nuint alignment,
            Action<T, nuint> lower,
            Action<nuint> release)
        {
            var buffer = CanonicalAbi.Allocate(size == 0 ? 1 : size, alignment);
            var lowered = false;
            try
            {
                lower(await value.ConfigureAwait(false), buffer);
                lowered = true;
                // As for a read: the reader may take the value only after a synchronous call has returned.
                var status = intrinsics.Write(writable, buffer);
                await PlatformServices.AwaitCopyOnEventLoop(writable, status).ConfigureAwait(false);
            }
            finally
            {
                if (lowered)
                {
                    release(buffer);
                }

                CanonicalAbi.Free(buffer);
                intrinsics.DropWritable(writable);
            }
        }
    }
}
