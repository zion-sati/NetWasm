// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

// The framework libraries' own async methods (System.Text.Json, System.IO.Pipelines, System.Threading.Channels,
// System.IO.Compression) under the event loop, beside CoreLib's (the corelib-async workload). Each read and write of
// a yielding stream suspends once, so the libraries' read and write loops suspend and resume inside them; the -ready
// scenarios read a stream that completes every read at once. Prints ASYNC|name|ticks|frequency|checksum and
// GC|collections|allocated|total, as the async workload does.
internal static class Program
{
    private static readonly Envelope Sample = new(
        "orders",
        [
            new("alpha", 3, 12.5m, ["red", "large"]),
            new("beta", 1, 7.25m, []),
            new("gamma delta", 12, 0.99m, ["blue"]),
            new("epsilon", 5, 120m, ["green", "small", "boxed"]),
        ],
        new Dictionary<string, int> { ["north"] = 4, ["south"] = 9 });

    private static readonly byte[] Json = JsonSerializer.SerializeToUtf8Bytes(Sample, WorkloadJsonContext.Default.Envelope);

    private static readonly byte[] Text = Encoding.UTF8.GetBytes(string.Concat(
        "the first line of the text\n", "a second line, a little longer than the first\n", "third\n", "\n",
        "a fifth line that runs on for a while before it ends\n", "six\n", "seven is short too\n",
        "the eighth line ends without the others' brevity, at some length\n", "nine\n", "and the last line\n"));

    private static readonly byte[] Deflated = Deflate(Text);

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0) return 2;
        Func<int, Task<long>>? scenario = args[0] switch
        {
            "json-write" => JsonWrites,
            "json-read" => JsonReads,
            "json-read-ready" => JsonReadsReady,
            "pipe" => Pipes,
            "channel" => Channels,
            "deflate-read" => DeflateReads,
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

    private static async Task<long> JsonWrites(int count)
    {
        var sink = new YieldingStream(Json, 64);
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            sink.Reset();
            await JsonSerializer.SerializeAsync(sink, Sample, WorkloadJsonContext.Default.Envelope);
            sum += sink.Written;
        }
        return sum;
    }

    private static async Task<long> JsonReads(int count)
    {
        var source = new YieldingStream(Json, 64);
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Reset();
            sum += Checksum(await JsonSerializer.DeserializeAsync(source, WorkloadJsonContext.Default.Envelope), index);
        }
        return sum;
    }

    private static async Task<long> JsonReadsReady(int count)
    {
        var source = new MemoryStream(Json, writable: false);
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Position = 0;
            sum += Checksum(await JsonSerializer.DeserializeAsync(source, WorkloadJsonContext.Default.Envelope), index);
        }
        return sum;
    }

    // A writer and a reader over one pipe: the writer writes four messages, flushing after each, while the reader
    // reads until it has seen them all, each flush resuming it.
    private static async Task<long> Pipes(int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false));
            var writing = WriteMessages(pipe.Writer, index);
            sum += await ReadMessages(pipe.Reader) + await writing;
        }
        return sum;
    }

    private static async Task<long> WriteMessages(PipeWriter writer, int seed)
    {
        for (var message = 0; message < 4; message++)
        {
            var memory = writer.GetMemory(48);
            Text.AsSpan(message * 11, 40).CopyTo(memory.Span);
            memory.Span[0] = (byte)(seed + message);
            writer.Advance(40);
            await Task.Yield();
            await writer.FlushAsync();
        }
        await writer.CompleteAsync();
        return seed & 7;
    }

    private static async Task<long> ReadMessages(PipeReader reader)
    {
        long sum = 0;
        while (true)
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;
            foreach (var segment in buffer)
            {
                foreach (var value in segment.Span) sum += value;
            }
            reader.AdvanceTo(buffer.End);
            if (result.IsCompleted) break;
        }
        await reader.CompleteAsync();
        return sum;
    }

    // A producer and a consumer over a bounded channel of four: the producer waits for room after every fourth item.
    private static async Task<long> Channels(int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(4) { SingleReader = true, SingleWriter = true });
            var producing = Produce(channel.Writer, index);
            long consumed = 0;
            while (await channel.Reader.WaitToReadAsync())
            {
                while (channel.Reader.TryRead(out var item)) consumed += item;
            }
            await producing;
            sum += consumed;
        }
        return sum;
    }

    private static async Task Produce(ChannelWriter<int> writer, int seed)
    {
        for (var item = 0; item < 16; item++)
        {
            await writer.WriteAsync(seed + item);
            if ((item & 3) == 3) await Task.Yield();
        }
        writer.Complete();
    }

    private static async Task<long> DeflateReads(int count)
    {
        var source = new YieldingStream(Deflated, 64);
        var buffer = new byte[96];
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            source.Reset();
            using var inflater = new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true);
            int read;
            while ((read = await inflater.ReadAsync(buffer)) > 0) sum += read + buffer[index % read];
        }
        return sum;
    }

    private static long Checksum(Envelope? envelope, int index)
    {
        if (envelope is null) return -1;
        long sum = envelope.Name.Length + envelope.Totals["south"];
        var line = envelope.Lines[index % envelope.Lines.Count];
        return sum + line.Name.Length + line.Quantity + (long)(line.Price * 100) + line.Tags.Count;
    }

    private static byte[] Deflate(byte[] data)
    {
        var output = new MemoryStream();
        using (var deflater = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var repeat = 0; repeat < 4; repeat++) deflater.Write(data);
        }
        return output.ToArray();
    }
}

internal sealed record Envelope(string Name, List<Line> Lines, Dictionary<string, int> Totals);

internal sealed record Line(string Name, int Quantity, decimal Price, List<string> Tags);

[JsonSerializable(typeof(Envelope))]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext;

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
