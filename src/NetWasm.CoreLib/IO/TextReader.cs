// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). MarshalByRefObject and
// remoting members are intentionally omitted for NetWasm; Task/ValueTask
// reads use the platform scheduler.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    // This abstract base class represents a reader that can read a sequential
    // stream of characters. A subclass must minimally implement Peek() and Read().
    public abstract class TextReader : IDisposable
    {
        public static readonly TextReader Null = new StreamReader.NullStreamReader();

        protected TextReader() { }

        public virtual void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }

        public virtual int Peek() => -1;

        public virtual int Read() => -1;

        public virtual int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            var read = 0;
            while (read < count)
            {
                var character = Read();
                if (character == -1)
                {
                    break;
                }

                buffer[index + read++] = (char)character;
            }

            return read;
        }

        public virtual int Read(Span<char> buffer)
        {
            var array = ArrayPool<char>.Shared.Rent(buffer.Length);
            try
            {
                var read = Read(array, 0, buffer.Length);
                if ((uint)read > (uint)buffer.Length)
                {
                    throw new IOException("The reader returned more characters than requested.");
                }

                new Span<char>(array, 0, read).CopyTo(buffer);
                return read;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        public virtual string ReadToEnd()
        {
            var chars = new char[4096];
            var builder = new StringBuilder(4096);
            int read;
            while ((read = Read(chars, 0, chars.Length)) != 0)
            {
                builder.Append(chars, 0, read);
            }

            return builder.ToString();
        }

        public virtual int ReadBlock(char[] buffer, int index, int count)
        {
            var total = 0;
            int read;
            do
            {
                read = Read(buffer, index + total, count - total);
                total += read;
            }
            while (read > 0 && total < count);

            return total;
        }

        public virtual int ReadBlock(Span<char> buffer)
        {
            var array = ArrayPool<char>.Shared.Rent(buffer.Length);
            try
            {
                var read = ReadBlock(array, 0, buffer.Length);
                if ((uint)read > (uint)buffer.Length)
                {
                    throw new IOException("The reader returned more characters than requested.");
                }

                new Span<char>(array, 0, read).CopyTo(buffer);
                return read;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        public virtual string? ReadLine()
        {
            var builder = new StringBuilder();
            while (true)
            {
                var character = Read();
                if (character == -1)
                {
                    break;
                }

                if (character == '\r' || character == '\n')
                {
                    if (character == '\r' && Peek() == '\n')
                    {
                        Read();
                    }

                    return builder.ToString();
                }

                builder.Append((char)character);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }

        public virtual Task<string?> ReadLineAsync() =>
            ReadLineAsync(CancellationToken.None).AsTask();

        public virtual ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            new(ScheduleAsync(static reader => reader.ReadLine(), this, cancellationToken));

        public virtual Task<string> ReadToEndAsync() => ReadToEndAsync(CancellationToken.None);

        public virtual Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
            ScheduleAsync(static reader => reader.ReadToEnd(), this, cancellationToken);

        public virtual Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            return ScheduleAsync(
                static state => state.Reader.Read(state.Buffer, state.Index, state.Count),
                new ReadOperation(this, buffer, index, count),
                CancellationToken.None);
        }

        public virtual ValueTask<int> ReadAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<char> array))
            {
                return new ValueTask<int>(ScheduleAsync(
                    static state => state.Reader.Read(state.Buffer, state.Index, state.Count),
                    new ReadOperation(this, array.Array!, array.Offset, array.Count),
                    cancellationToken));
            }

            return new ValueTask<int>(ScheduleAsync(
                static state => state.Reader.Read(state.Memory.Span),
                new MemoryReadOperation(this, buffer),
                cancellationToken));
        }

        public virtual Task<int> ReadBlockAsync(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            return ScheduleAsync(
                static state => state.Reader.ReadBlock(state.Buffer, state.Index, state.Count),
                new ReadOperation(this, buffer, index, count),
                CancellationToken.None);
        }

        public virtual ValueTask<int> ReadBlockAsync(
            Memory<char> buffer,
            CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<char> array))
            {
                return new ValueTask<int>(ScheduleAsync(
                    static state => state.Reader.ReadBlock(state.Buffer, state.Index, state.Count),
                    new ReadOperation(this, array.Array!, array.Offset, array.Count),
                    cancellationToken));
            }

            return new ValueTask<int>(ScheduleAsync(
                static state => state.Reader.ReadBlock(state.Memory.Span),
                new MemoryReadOperation(this, buffer),
                cancellationToken));
        }

        public static TextReader Synchronized(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return reader is SyncTextReader ? reader : new SyncTextReader(reader);
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

        private sealed class ReadOperation
        {
            internal ReadOperation(TextReader reader, char[] buffer, int index, int count)
            {
                Reader = reader;
                Buffer = buffer;
                Index = index;
                Count = count;
            }

            internal TextReader Reader { get; }
            internal char[] Buffer { get; }
            internal int Index { get; }
            internal int Count { get; }
        }

        private sealed class MemoryReadOperation
        {
            internal MemoryReadOperation(TextReader reader, Memory<char> memory)
            {
                Reader = reader;
                Memory = memory;
            }

            internal TextReader Reader { get; }
            internal Memory<char> Memory { get; }
        }

        internal sealed class SyncTextReader : TextReader
        {
            internal readonly TextReader Reader;

            internal SyncTextReader(TextReader reader) => Reader = reader;

            [MethodImpl(MethodImplOptions.Synchronized)] public override void Close() => Reader.Close();
            [MethodImpl(MethodImplOptions.Synchronized)]
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    ((IDisposable)Reader).Dispose();
                }
            }
            [MethodImpl(MethodImplOptions.Synchronized)] public override int Peek() => Reader.Peek();
            [MethodImpl(MethodImplOptions.Synchronized)] public override int Read() => Reader.Read();
            [MethodImpl(MethodImplOptions.Synchronized)] public override int Read(char[] buffer, int index, int count) => Reader.Read(buffer, index, count);
            [MethodImpl(MethodImplOptions.Synchronized)] public override int Read(Span<char> buffer) => Reader.Read(buffer);
            [MethodImpl(MethodImplOptions.Synchronized)] public override int ReadBlock(char[] buffer, int index, int count) => Reader.ReadBlock(buffer, index, count);
            [MethodImpl(MethodImplOptions.Synchronized)] public override int ReadBlock(Span<char> buffer) => Reader.ReadBlock(buffer);
            [MethodImpl(MethodImplOptions.Synchronized)] public override string? ReadLine() => Reader.ReadLine();
            [MethodImpl(MethodImplOptions.Synchronized)] public override string ReadToEnd() => Reader.ReadToEnd();

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override Task<string?> ReadLineAsync() => Task.FromResult(ReadLine());

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? ValueTask.FromCanceled<string?>(cancellationToken)
                    : new ValueTask<string?>(ReadLine());

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override Task<string> ReadToEndAsync() => Task.FromResult(ReadToEnd());

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled<string>(cancellationToken)
                    : Task.FromResult(ReadToEnd());

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override Task<int> ReadAsync(char[] buffer, int index, int count) =>
                Task.FromResult(Read(buffer, index, count));

            [MethodImpl(MethodImplOptions.Synchronized)]
            public override Task<int> ReadBlockAsync(char[] buffer, int index, int count) =>
                Task.FromResult(ReadBlock(buffer, index, count));
        }
    }
}
