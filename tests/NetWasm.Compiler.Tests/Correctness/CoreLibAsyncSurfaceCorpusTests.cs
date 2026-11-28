// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;

namespace NetWasm.Compiler.Tests.Correctness;

// CoreLib's own async methods (streams, readers and writers, SemaphoreSlim) against desktop .NET: with CoreLib compiled
// with runtime async, as upstream's is, these are the methods the lowering rewrites inside CoreLib. A gated stream's
// reads stay pending until the fixture opens their gates one at a time, so CopyToAsync, ReadToEndAsync,
// ReadExactlyAsync and the rest really suspend and resume, without an event loop. Each case returns a hash of what it
// read, wrote or observed.
public sealed class CoreLibAsyncSurfaceCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void CoreLibAsyncMethodsMatchDesktop() =>
        runner.Run(new(
            "CoreLibAsyncSurfaceCorpus",
            "NetWasm.Correctness.CoreLibAsyncSurface",
            Source,
            [.. Enumerable.Range(0, 12)]));

    internal const string Source = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Text;
        using System.Threading;
        using System.Threading.Tasks;

        namespace NetWasm.Correctness.CoreLibAsyncSurface;

        // A stream whose every read waits on a gate the fixture opens, returning at most seven bytes.
        public sealed class GatedStream : Stream
        {
            private readonly byte[] _data;
            private int _position;
            public readonly Queue<TaskCompletionSource<bool>> Gates = new();

            public GatedStream(byte[] data) => _data = data;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var gate = new TaskCompletionSource<bool>();
                Gates.Enqueue(gate);
                return new ValueTask<int>(ReadWhenOpen(gate.Task, buffer));
            }

            private async Task<int> ReadWhenOpen(Task<bool> gate, Memory<byte> buffer)
            {
                await gate;
                var count = Math.Min(buffer.Length, Math.Min(7, _data.Length - _position));
                _data.AsSpan(_position, count).CopyTo(buffer.Span);
                _position += count;
                return count;
            }

            // Opens the gates as the reads ask for them until the task is done.
            public T Drive<T>(Task<T> task)
            {
                while (!task.IsCompleted)
                {
                    if (Gates.Count == 0) throw new InvalidOperationException("stalled");
                    Gates.Dequeue().SetResult(true);
                }
                return task.GetAwaiter().GetResult();
            }
        }

        public static class EntryPoint
        {
            private static readonly byte[] Text = Encoding.UTF8.GetBytes(
                "first line\nsecond, longer line\nthird\n\nlast line without a newline, longer than seven bytes");

            public static int Run(int input) => input switch
            {
                0 => MemoryRoundTrip().GetAwaiter().GetResult(),
                1 => ReaderOverMemory().GetAwaiter().GetResult(),
                2 => WriterIntoMemory().GetAwaiter().GetResult(),
                3 => CopyFromGated(),
                4 => ReadToEndFromGated(),
                5 => ReadExactlyFromGated(),
                6 => BufferedOverGated(),
                7 => SemaphoreCounts(),
                8 => SemaphoreCancellation(),
                9 => Disposed().GetAwaiter().GetResult(),
                10 => StringReaderBlocks().GetAwaiter().GetResult(),
                11 => WhenAllWrites().GetAwaiter().GetResult(),
                _ => -1,
            };

            public static int Trace() => 0;

            private static int Hash(ReadOnlySpan<byte> bytes)
            {
                var hash = 17;
                foreach (var value in bytes) hash = unchecked(hash * 31 + value);
                return hash;
            }

            private static int Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));

            private static async Task<int> MemoryRoundTrip()
            {
                var stream = new MemoryStream();
                await stream.WriteAsync(Text);
                await stream.WriteAsync(Text, 3, 9);
                await stream.FlushAsync();
                stream.Position = 0;
                var buffer = new byte[stream.Length];
                var read = 0;
                while (read < buffer.Length) read += await stream.ReadAsync(buffer.AsMemory(read));
                return Hash(buffer) ^ read;
            }

            private static async Task<int> ReaderOverMemory()
            {
                using var reader = new StreamReader(new MemoryStream(Text));
                var first = await reader.ReadLineAsync();
                var rest = await reader.ReadToEndAsync();
                return Hash(first!) * 7 + Hash(rest);
            }

            private static async Task<int> WriterIntoMemory()
            {
                var stream = new MemoryStream();
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 16, leaveOpen: true))
                {
                    await writer.WriteAsync("alpha");
                    await writer.WriteLineAsync(" beta");
                    await writer.WriteAsync('!');
                    await writer.WriteLineAsync("gamma delta epsilon".AsMemory());
                    await writer.FlushAsync();
                }
                return Hash(stream.ToArray()) ^ (int)stream.Length;
            }

            private static int CopyFromGated()
            {
                var source = new GatedStream(Text);
                var destination = new MemoryStream();
                source.Drive(Copy(source, destination));
                return Hash(destination.ToArray());
            }

            private static async Task<bool> Copy(Stream source, Stream destination)
            {
                await source.CopyToAsync(destination, 5);
                return true;
            }

            private static int ReadToEndFromGated()
            {
                var source = new GatedStream(Text);
                var reader = new StreamReader(source, Encoding.UTF8, false, 16);
                return Hash(source.Drive(reader.ReadToEndAsync()));
            }

            private static int ReadExactlyFromGated()
            {
                var source = new GatedStream(Text);
                var buffer = new byte[20];
                source.Drive(ReadExactly(source, buffer));
                var atLeast = new byte[30];
                var read = source.Drive(source.ReadAtLeastAsync(atLeast, 10).AsTask());
                return Hash(buffer) * 3 + Hash(atLeast.AsSpan(0, read)) + read;
            }

            private static async Task<bool> ReadExactly(Stream source, byte[] buffer)
            {
                await source.ReadExactlyAsync(buffer);
                return true;
            }

            private static int BufferedOverGated()
            {
                var source = new GatedStream(Text);
                var buffered = new BufferedStream(source, 11);
                var buffer = new byte[64];
                var total = source.Drive(ReadAll(buffered, buffer));
                return Hash(buffer.AsSpan(0, total)) + total;
            }

            private static async Task<int> ReadAll(Stream stream, byte[] buffer)
            {
                var total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(total, Math.Min(9, buffer.Length - total)))) > 0)
                    total += read;
                return total;
            }

            // Waits that complete at once, or time out at once: SemaphoreSlim completes a waiter it releases later
            // asynchronously, which only an event loop would run.
            private static int SemaphoreCounts()
            {
                var semaphore = new SemaphoreSlim(2);
                var first = semaphore.WaitAsync();
                var second = semaphore.WaitAsync(0);
                var third = semaphore.WaitAsync(TimeSpan.Zero);
                var entered = first.IsCompletedSuccessfully ? 1 : 0;
                var released = semaphore.Release(2);
                return entered * 10000 + (second.GetAwaiter().GetResult() ? 1000 : 0) +
                    (third.GetAwaiter().GetResult() ? 100 : 0) + released * 10 + semaphore.CurrentCount;
            }

            // A wait on a token already canceled ends canceled at once; one canceled later completes asynchronously,
            // which only an event loop would run, and blocking on it is not supported on one thread.
            private static int SemaphoreCancellation()
            {
                var semaphore = new SemaphoreSlim(0);
                var source = new CancellationTokenSource();
                source.Cancel();
                var waiting = semaphore.WaitAsync(source.Token);
                var kind = 0;
                try
                {
                    waiting.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == source.Token)
                {
                    kind = 2;
                }
                catch (OperationCanceledException)
                {
                    kind = 3;
                }
                return (waiting.IsCompleted ? 100 : 0) + kind * 10 + (waiting.IsCanceled ? 1 : 0);
            }

            private static async Task<int> Disposed()
            {
                var stream = new MemoryStream(Text);
                stream.Dispose();
                try
                {
                    await stream.ReadAsync(new byte[4]);
                    return 0;
                }
                catch (ObjectDisposedException)
                {
                    return 1;
                }
            }

            private static async Task<int> StringReaderBlocks()
            {
                var reader = new StringReader("one two three four five six seven");
                var block = new char[10];
                var read = await reader.ReadBlockAsync(block, 0, block.Length);
                var line = await reader.ReadLineAsync();
                var writer = new StringWriter();
                await writer.WriteAsync(block, 0, read);
                await writer.WriteLineAsync(line);
                return Hash(writer.ToString()) + read;
            }

            private static async Task<int> WhenAllWrites()
            {
                var streams = new MemoryStream[3];
                var writes = new Task[3];
                for (var index = 0; index < streams.Length; index++)
                {
                    streams[index] = new MemoryStream();
                    writes[index] = streams[index].WriteAsync(Text, index, Text.Length - index);
                }
                await Task.WhenAll(writes);
                var hash = 0;
                foreach (var stream in streams) hash = hash * 31 + Hash(stream.ToArray());
                return hash;
            }
        }
        """;
}
