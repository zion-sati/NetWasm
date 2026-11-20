// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Security
{
    // What SslStream writes, as the stream wasi:tls's send reads its cleartext from: a write waits until the reader
    // has taken all of it, so the caller's buffer is never read after the write completes. One writer and one
    // reader; Complete ends the data, which closes the session's sending side.
    internal sealed class TlsOutgoingPipe : Stream
    {
        private ReadOnlyMemory<byte> _pending;
        private TaskCompletionSource? _taken;
        private TaskCompletionSource? _available;
        private bool _completed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_pending.IsEmpty) return new ValueTask<int>(Take(buffer));
            if (_completed || buffer.IsEmpty) return new ValueTask<int>(0);
            return WaitForDataAsync(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_completed) throw new InvalidOperationException(SR.net_ssl_io_already_shutdown);
            if (buffer.IsEmpty) return;
            var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = buffer;
            _taken = taken;
            Signal(ref _available);
            try
            {
                await taken.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The reader must not see the caller's buffer once the write has returned.
                _pending = default;
                _taken = null;
                throw;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public void Complete()
        {
            _completed = true;
            Signal(ref _available);
        }

        private async ValueTask<int> WaitForDataAsync(Memory<byte> buffer)
        {
            var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _available = available;
            await available.Task.ConfigureAwait(false);
            return _pending.IsEmpty ? 0 : Take(buffer);
        }

        private int Take(Memory<byte> buffer)
        {
            var count = Math.Min(buffer.Length, _pending.Length);
            _pending.Span.Slice(0, count).CopyTo(buffer.Span);
            _pending = _pending.Slice(count);
            if (_pending.IsEmpty) Signal(ref _taken);
            return count;
        }

        private static void Signal(ref TaskCompletionSource? waiter)
        {
            var current = waiter;
            waiter = null;
            current?.TrySetResult();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
