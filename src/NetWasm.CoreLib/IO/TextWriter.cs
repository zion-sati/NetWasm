// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime System.Private.CoreLib (commit
// 811225a482702af7ecc35d817966bc70b88a3a23). MarshalByRefObject is
// intentionally omitted; Task/ValueTask writes use the platform scheduler.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    public abstract class TextWriter : IDisposable, IAsyncDisposable
    {
        public static readonly TextWriter Null = new NullTextWriter();

        private const string NewLineConst = "\n";
        private static readonly char[] s_coreNewLine = NewLineConst.ToCharArray();
        private static char[]? s_otherCoreNewLine;

        protected char[] CoreNewLine = s_coreNewLine;
        private string _coreNewLineStr = NewLineConst;
        private readonly IFormatProvider? _internalFormatProvider;

        protected TextWriter() { }
        protected TextWriter(IFormatProvider? formatProvider) => _internalFormatProvider = formatProvider;

        public virtual IFormatProvider FormatProvider => _internalFormatProvider!;

        public virtual void Close()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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

        public virtual void Flush() { }

        public abstract Encoding Encoding { get; }

        public virtual string NewLine
        {
            get => _coreNewLineStr;
            set
            {
                value ??= NewLineConst;
                if (_coreNewLineStr == value)
                {
                    return;
                }

                _coreNewLineStr = value;
                CoreNewLine = value == NewLineConst
                    ? s_coreNewLine
                    : NewLineConst == "\r\n" && value == "\n"
                        ? s_otherCoreNewLine ??= ['\n']
                        : NewLineConst == "\n" && value == "\r\n"
                            ? s_otherCoreNewLine ??= ['\r', '\n']
                            : value.ToCharArray();
            }
        }

        public virtual void Write(char value) { }

        public virtual void Write(Text.Rune value)
        {
            Span<char> chars = stackalloc char[2];
            var length = value.EncodeToUtf16(chars);
            Write(chars[..length]);
        }

        public virtual void Write(char[]? buffer)
        {
            if (buffer is not null)
            {
                Write(buffer, 0, buffer.Length);
            }
        }

        public virtual void Write(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("Offset and length were out of bounds for the array.");
            }

            for (var offset = 0; offset < count; offset++)
            {
                Write(buffer[index + offset]);
            }
        }

        public virtual void Write(ReadOnlySpan<char> buffer)
        {
            var array = ArrayPool<char>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(array);
                Write(array, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        public virtual void Write(bool value) => Write(value ? "True" : "False");
        public virtual void Write(int value) => Write(value.ToString(FormatProvider));
        public virtual void Write(uint value) => Write(value.ToString(FormatProvider));
        public virtual void Write(long value) => Write(value.ToString(FormatProvider));
        public virtual void Write(ulong value) => Write(value.ToString(FormatProvider));
        public virtual void Write(float value) => Write(value.ToString(FormatProvider));
        public virtual void Write(double value) => Write(value.ToString(FormatProvider));
        public virtual void Write(decimal value) => Write(value.ToString(FormatProvider));

        public virtual void Write(string? value)
        {
            if (value is not null)
            {
                Write(value.ToCharArray());
            }
        }

        public virtual void Write(object? value)
        {
            if (value is IFormattable formattable)
            {
                Write(formattable.ToString(null, FormatProvider));
            }
            else if (value is not null)
            {
                Write(value.ToString());
            }
        }

        public virtual void Write(Text.StringBuilder? value)
        {
            if (value is not null)
            {
                foreach (ReadOnlyMemory<char> chunk in value.GetChunks())
                {
                    Write(chunk.Span);
                }
            }
        }

        public virtual void Write(string format, object? arg0) => Write(string.Format(FormatProvider, format, arg0));
        public virtual void Write(string format, object? arg0, object? arg1) => Write(string.Format(FormatProvider, format, arg0, arg1));
        public virtual void Write(string format, object? arg0, object? arg1, object? arg2) => Write(string.Format(FormatProvider, format, arg0, arg1, arg2));
        public virtual void Write(string format, params object?[] arg) => Write(string.Format(FormatProvider, format, arg));
        public virtual void Write(string format, params ReadOnlySpan<object?> arg) => Write(string.Format(FormatProvider, format, arg));

        public virtual void WriteLine() => Write(CoreNewLine);
        public virtual void WriteLine(char value) { Write(value); WriteLine(); }

        public virtual void WriteLine(Text.Rune value)
        {
            Span<char> chars = stackalloc char[2];
            var length = value.EncodeToUtf16(chars);
            Write(chars[..length]);
            WriteLine();
        }

        public virtual void WriteLine(char[]? buffer) { Write(buffer); WriteLine(); }
        public virtual void WriteLine(char[] buffer, int index, int count) { Write(buffer, index, count); WriteLine(); }

        public virtual void WriteLine(ReadOnlySpan<char> buffer)
        {
            var array = ArrayPool<char>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(array);
                WriteLine(array, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(array);
            }
        }

        public virtual void WriteLine(bool value) { Write(value); WriteLine(); }
        public virtual void WriteLine(int value) { Write(value); WriteLine(); }
        public virtual void WriteLine(uint value) { Write(value); WriteLine(); }
        public virtual void WriteLine(long value) { Write(value); WriteLine(); }
        public virtual void WriteLine(ulong value) { Write(value); WriteLine(); }
        public virtual void WriteLine(float value) { Write(value); WriteLine(); }
        public virtual void WriteLine(double value) { Write(value); WriteLine(); }
        public virtual void WriteLine(decimal value) { Write(value); WriteLine(); }

        public virtual void WriteLine(string? value)
        {
            if (value is not null)
            {
                Write(value);
            }

            Write(_coreNewLineStr);
        }

        public virtual void WriteLine(Text.StringBuilder? value) { Write(value); WriteLine(); }

        public virtual void WriteLine(object? value)
        {
            if (value is IFormattable formattable)
            {
                WriteLine(formattable.ToString(null, FormatProvider));
            }
            else if (value is not null)
            {
                WriteLine(value.ToString());
            }
            else
            {
                WriteLine();
            }
        }

        public virtual void WriteLine(string format, object? arg0) => WriteLine(string.Format(FormatProvider, format, arg0));
        public virtual void WriteLine(string format, object? arg0, object? arg1) => WriteLine(string.Format(FormatProvider, format, arg0, arg1));
        public virtual void WriteLine(string format, object? arg0, object? arg1, object? arg2) => WriteLine(string.Format(FormatProvider, format, arg0, arg1, arg2));
        public virtual void WriteLine(string format, params object?[] arg) => WriteLine(string.Format(FormatProvider, format, arg));
        public virtual void WriteLine(string format, params ReadOnlySpan<object?> arg) => WriteLine(string.Format(FormatProvider, format, arg));

        public virtual Task WriteAsync(char value) =>
            ScheduleAsync(static state => state.Writer.Write(state.Value), new WriteValue(this, value), CancellationToken.None);

        public virtual Task WriteAsync(Text.Rune value) =>
            ScheduleAsync(static state => state.Writer.Write(state.Value), new WriteRune(this, value), CancellationToken.None);

        public virtual Task WriteAsync(string? value) =>
            ScheduleAsync(static state => state.Writer.Write(state.Value), new WriteString(this, value), CancellationToken.None);

        public Task WriteAsync(string? value, CancellationToken cancellationToken) =>
            ScheduleAsync(static state => state.Writer.Write(state.Value), new WriteString(this, value), cancellationToken);

        public virtual Task WriteAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) =>
            ScheduleAsync(static state => state.Writer.Write(state.Value), new WriteStringBuilder(this, value), cancellationToken);

        public Task WriteAsync(char[]? buffer) =>
            buffer is null
                ? Task.CompletedTask
                : WriteAsync(buffer, 0, buffer.Length);

        public virtual Task WriteAsync(char[] buffer, int index, int count) =>
            ScheduleAsync(
                static state => state.Writer.Write(state.Buffer, state.Index, state.Count),
                new WriteArray(this, buffer, index, count),
                CancellationToken.None);

        public virtual Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<char> array))
            {
                return ScheduleAsync(
                    static state => state.Writer.Write(state.Buffer, state.Index, state.Count),
                    new WriteArray(this, array.Array!, array.Offset, array.Count),
                    cancellationToken);
            }

            return ScheduleAsync(
                static state => state.Writer.Write(state.Memory.Span),
                new WriteMemory(this, buffer),
                cancellationToken);
        }

        public virtual Task WriteLineAsync(char value) =>
            ScheduleAsync(static state => state.Writer.WriteLine(state.Value), new WriteValue(this, value), CancellationToken.None);

        public virtual Task WriteLineAsync(Text.Rune value) =>
            ScheduleAsync(static state => state.Writer.WriteLine(state.Value), new WriteRune(this, value), CancellationToken.None);

        public virtual Task WriteLineAsync(string? value) =>
            ScheduleAsync(static state => state.Writer.WriteLine(state.Value), new WriteString(this, value), CancellationToken.None);

        public Task WriteLineAsync(string? value, CancellationToken cancellationToken) =>
            ScheduleAsync(static state => state.Writer.WriteLine(state.Value), new WriteString(this, value), cancellationToken);

        public virtual Task WriteLineAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) =>
            ScheduleAsync(static state => state.Writer.WriteLine(state.Value), new WriteStringBuilder(this, value), cancellationToken);

        public Task WriteLineAsync(char[]? buffer)
        {
            if (buffer is null)
            {
                return WriteLineAsync();
            }

            return WriteLineAsync(buffer, 0, buffer.Length);
        }

        public virtual Task WriteLineAsync(char[] buffer, int index, int count) =>
            ScheduleAsync(
                static state => state.Writer.WriteLine(state.Buffer, state.Index, state.Count),
                new WriteArray(this, buffer, index, count),
                CancellationToken.None);

        public virtual Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<char> array))
            {
                return ScheduleAsync(
                    static state => state.Writer.WriteLine(state.Buffer, state.Index, state.Count),
                    new WriteArray(this, array.Array!, array.Offset, array.Count),
                    cancellationToken);
            }

            return ScheduleAsync(
                static state => state.Writer.WriteLine(state.Memory.Span),
                new WriteMemory(this, buffer),
                cancellationToken);
        }

        public virtual Task WriteLineAsync() =>
            ScheduleAsync(static writer => writer.WriteLine(), this, CancellationToken.None);

        public Task WriteLineAsync(CancellationToken cancellationToken) =>
            ScheduleAsync(static writer => writer.WriteLine(), this, cancellationToken);

        public virtual Task FlushAsync() =>
            ScheduleAsync(static writer => writer.Flush(), this, CancellationToken.None);

        public virtual Task FlushAsync(CancellationToken cancellationToken) =>
            ScheduleAsync(static writer => writer.Flush(), this, cancellationToken);

        public static TextWriter Synchronized(TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            return !RuntimeFeature.IsMultithreadingSupported || writer is SyncTextWriter
                ? writer
                : new SyncTextWriter(writer);
        }

        private static Task ScheduleAsync<TState>(
            Action<TState> operation,
            TState state,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            var task = new Task();
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
                        operation(state);
                        task.TrySetResult();
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

        private sealed class WriteValue
        {
            internal WriteValue(TextWriter writer, char value) { Writer = writer; Value = value; }
            internal TextWriter Writer { get; }
            internal char Value { get; }
        }

        private sealed class WriteRune
        {
            internal WriteRune(TextWriter writer, Text.Rune value) { Writer = writer; Value = value; }
            internal TextWriter Writer { get; }
            internal Text.Rune Value { get; }
        }

        private sealed class WriteString
        {
            internal WriteString(TextWriter writer, string? value) { Writer = writer; Value = value; }
            internal TextWriter Writer { get; }
            internal string? Value { get; }
        }

        private sealed class WriteStringBuilder
        {
            internal WriteStringBuilder(TextWriter writer, Text.StringBuilder? value) { Writer = writer; Value = value; }
            internal TextWriter Writer { get; }
            internal Text.StringBuilder? Value { get; }
        }

        private sealed class WriteArray
        {
            internal WriteArray(TextWriter writer, char[] buffer, int index, int count)
            {
                Writer = writer;
                Buffer = buffer;
                Index = index;
                Count = count;
            }

            internal TextWriter Writer { get; }
            internal char[] Buffer { get; }
            internal int Index { get; }
            internal int Count { get; }
        }

        private sealed class WriteMemory
        {
            internal WriteMemory(TextWriter writer, ReadOnlyMemory<char> memory) { Writer = writer; Memory = memory; }
            internal TextWriter Writer { get; }
            internal ReadOnlyMemory<char> Memory { get; }
        }

        private sealed class NullTextWriter : TextWriter
        {
            public override IFormatProvider FormatProvider => null!;
            public override Encoding Encoding => Encoding.Unicode;
            public override string NewLine { get => base.NewLine; set { } }
            public override void Flush() { }
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
            public override void WriteLine(string? value) { }
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
            public override Task FlushAsync() => Task.CompletedTask;
            public override Task FlushAsync(CancellationToken cancellationToken) =>
                cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
        }

        internal sealed class SyncTextWriter : TextWriter
        {
            private readonly TextWriter _writer;
            internal SyncTextWriter(TextWriter writer) => _writer = writer;
            public override Encoding Encoding => _writer.Encoding;
            public override IFormatProvider FormatProvider => _writer.FormatProvider;
            public override string NewLine { [MethodImpl(MethodImplOptions.Synchronized)] get => _writer.NewLine; [MethodImpl(MethodImplOptions.Synchronized)] set => _writer.NewLine = value; }
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Close() => _writer.Close();
            [MethodImpl(MethodImplOptions.Synchronized)] protected override void Dispose(bool disposing) { if (disposing) ((IDisposable)_writer).Dispose(); }
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Flush() => _writer.Flush();
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(char value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(Text.Rune value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(char[]? value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(char[] value, int index, int count) => _writer.Write(value, index, count);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(ReadOnlySpan<char> value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(bool value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(int value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(uint value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(long value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(ulong value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(float value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(double value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(decimal value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(string? value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(object? value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(Text.StringBuilder? value) => _writer.Write(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(string format, object? arg0) => _writer.Write(format, arg0);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(string format, object? arg0, object? arg1) => _writer.Write(format, arg0, arg1);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(string format, object? arg0, object? arg1, object? arg2) => _writer.Write(format, arg0, arg1, arg2);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(string format, params object?[] arg) => _writer.Write(format, arg);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void Write(string format, params ReadOnlySpan<object?> arg) => _writer.Write(format, arg);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine() => _writer.WriteLine();
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(char value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(Text.Rune value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(char[]? value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(char[] value, int index, int count) => _writer.WriteLine(value, index, count);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(ReadOnlySpan<char> value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(bool value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(int value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(uint value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(long value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(ulong value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(float value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(double value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(decimal value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(string? value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(Text.StringBuilder? value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(object? value) => _writer.WriteLine(value);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(string format, object? arg0) => _writer.WriteLine(format, arg0);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(string format, object? arg0, object? arg1) => _writer.WriteLine(format, arg0, arg1);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(string format, object? arg0, object? arg1, object? arg2) => _writer.WriteLine(format, arg0, arg1, arg2);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(string format, params object?[] arg) => _writer.WriteLine(format, arg);
            [MethodImpl(MethodImplOptions.Synchronized)] public override void WriteLine(string format, params ReadOnlySpan<object?> arg) => _writer.WriteLine(format, arg);

            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteAsync(char value) => Task.FromResult(WriteAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteAsync(Text.Rune value) => Task.FromResult(WriteAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteAsync(string? value) => Task.FromResult(WriteAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) => Task.FromResult(WriteAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteAsync(char[] buffer, int index, int count) => Task.FromResult(WriteAndReturn(buffer, index, count));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default) => Task.FromResult(WriteAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync(char value) => Task.FromResult(WriteLineAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync(Text.Rune value) => Task.FromResult(WriteLineAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync(string? value) => Task.FromResult(WriteLineAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync(Text.StringBuilder? value, CancellationToken cancellationToken = default) => Task.FromResult(WriteLineAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync(char[] buffer, int index, int count) => Task.FromResult(WriteLineAndReturn(buffer, index, count));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default) => Task.FromResult(WriteLineAndReturn(value));
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task WriteLineAsync() => Task.FromResult(WriteLineAndReturn());
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task FlushAsync() => Task.FromResult(FlushAndReturn());
            [MethodImpl(MethodImplOptions.Synchronized)] public override Task FlushAsync(CancellationToken cancellationToken) => cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.FromResult(FlushAndReturn());

            private bool WriteAndReturn(char value) { _writer.Write(value); return true; }
            private bool WriteAndReturn(Text.Rune value) { _writer.Write(value); return true; }
            private bool WriteAndReturn(string? value) { _writer.Write(value); return true; }
            private bool WriteAndReturn(Text.StringBuilder? value) { _writer.Write(value); return true; }
            private bool WriteAndReturn(char[] value, int index, int count) { _writer.Write(value, index, count); return true; }
            private bool WriteAndReturn(ReadOnlyMemory<char> value) { _writer.Write(value.Span); return true; }
            private bool WriteLineAndReturn(char value) { _writer.WriteLine(value); return true; }
            private bool WriteLineAndReturn(Text.Rune value) { _writer.WriteLine(value); return true; }
            private bool WriteLineAndReturn(string? value) { _writer.WriteLine(value); return true; }
            private bool WriteLineAndReturn(Text.StringBuilder? value) { _writer.WriteLine(value); return true; }
            private bool WriteLineAndReturn(char[] value, int index, int count) { _writer.WriteLine(value, index, count); return true; }
            private bool WriteLineAndReturn(ReadOnlyMemory<char> value) { _writer.WriteLine(value.Span); return true; }
            private bool WriteLineAndReturn() { _writer.WriteLine(); return true; }
            private bool FlushAndReturn() { _writer.Flush(); return true; }
        }
    }
}
