// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// CoreLib's own async methods under the event loop. Every read and write of a yielding stream suspends once, so
// CoreLib's copy, read and write loops (Stream.CopyToAsync, StreamReader, StreamWriter, BufferedStream,
// ReadExactlyAsync) and SemaphoreSlim's cancelable wait suspend and resume inside CoreLib. Prints
// ASYNC|name|ticks|frequency|checksum and GC|collections|allocated|total, as the async workload does.
internal static class Program
{
    private static readonly byte[] Text = Encoding.UTF8.GetBytes(string.Concat(
        "the first line of the text\n", "a second line, a little longer than the first\n", "third\n", "\n",
        "a fifth line that runs on for a while before it ends\n", "six\n", "seven is short too\n",
        "the eighth line ends without the others' brevity, at some length\n", "nine\n", "and the last line\n"));

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0) return 2;
        Func<int, Task<long>>? scenario = args[0] switch
        {
            "copy" => Copies,
            "read-lines" => ReadLines,
            "read-exactly" => ReadExactly,
            "buffered" => BufferedReads,
            "writer" => Writes,
            "semaphore" => Semaphores,
            _ => null,
        };
        if (scenario is null) return 3;
        for (var warmup = 0; warmup < 3; warmup++) await scenario(count);

        _ = Stopwatch.GetTimestamp();
        _ = GC.CollectionCount(0);
        _ = GC.GetTotalAllocatedBytes(true);
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        var checksum = await scenario(count);
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var collections = GC.CollectionCount(0) - collectionsBefore;
        Console.WriteLine(string.Join("|", "ASYNC", args[0], elapsed.ToString(), Stopwatch.Frequency.ToString(),
            checksum.ToString()));
        Console.WriteLine(string.Join("|", "GC", collections.ToString(), allocated.ToString(),
            GC.GetTotalMemory(false).ToString()));
        return allocatedBefore <= 0 ? 6 : 0;
    }

    private static async Task<long> Copies(int count)
    {
        var source = new YieldingStream(Text, 64);
        var destination = new MemoryStream();
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Reset();
            destination.Position = 0;
            await source.CopyToAsync(destination, 48);
            sum += destination.Position + destination.GetBuffer()[index % Text.Length];
        }
        return sum;
    }

    private static async Task<long> ReadLines(int count)
    {
        var source = new YieldingStream(Text, 64);
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Reset();
            using var reader = new StreamReader(source, Encoding.UTF8, false, 128, leaveOpen: true);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null) sum += line.Length + 1;
        }
        return sum;
    }

    private static async Task<long> ReadExactly(int count)
    {
        var source = new YieldingStream(Text, 64);
        var buffer = new byte[240];
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Reset();
            await source.ReadExactlyAsync(buffer);
            sum += buffer[index % buffer.Length];
        }
        return sum;
    }

    private static async Task<long> BufferedReads(int count)
    {
        var source = new YieldingStream(Text, 64);
        var buffer = new byte[37];
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Reset();
            var buffered = new BufferedStream(source, 100);
            int read;
            while ((read = await buffered.ReadAsync(buffer)) > 0) sum += read + buffer[0];
        }
        return sum;
    }

    private static async Task<long> Writes(int count)
    {
        var sink = new YieldingStream(Text, 64);
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            sink.Reset();
            var writer = new StreamWriter(sink, new UTF8Encoding(false), 64, leaveOpen: true);
            await writer.WriteLineAsync("alpha beta gamma delta");
            await writer.WriteAsync("epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho");
            await writer.WriteLineAsync(index.ToString());
            await writer.FlushAsync();
            sum += sink.Written;
        }
        return sum;
    }

    private static async Task<long> Semaphores(int count)
    {
        var semaphore = new SemaphoreSlim(0);
        var cancellation = new CancellationTokenSource();
        var waiters = new Task[8];
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            for (var waiter = 0; waiter < waiters.Length; waiter++) waiters[waiter] = semaphore.WaitAsync(cancellation.Token);
            semaphore.Release(waiters.Length);
            await Task.WhenAll(waiters);
            sum += semaphore.CurrentCount + 1;
        }
        return sum;
    }
}

// A stream whose reads and writes each yield once before they complete, reading at most a chunk at a time.
internal sealed class YieldingStream(byte[] data, int chunk) : Stream
{
    private int _position;

    public long Written { get; private set; }

    public void Reset()
    {
        _position = 0;
        Written = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var count = Math.Min(Math.Min(buffer.Length, chunk), data.Length - _position);
        data.AsSpan(_position, count).CopyTo(buffer.Span);
        _position += count;
        return count;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        Written += buffer.Length + (buffer.Length > 0 ? buffer.Span[0] : 0);
    }
}
