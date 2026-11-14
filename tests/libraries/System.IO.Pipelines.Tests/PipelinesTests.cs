using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.IO.Pipelines.Tests;

public sealed class PipelinesTests
{
    [Test]
    public async Task WriteFlushReadAndCompletionPreserveTheBufferContract()
    {
        var pipe = new Pipe(CreateOptions());
        var memory = pipe.Writer.GetMemory(5);
        memory.Span[0] = 1;
        memory.Span[1] = 2;
        memory.Span[2] = 3;
        memory.Span[3] = 4;
        memory.Span[4] = 5;
        pipe.Writer.Advance(5);

        await Assert.That(pipe.Writer.CanGetUnflushedBytes).IsTrue();
        await Assert.That(pipe.Writer.UnflushedBytes).IsEqualTo(5);
        var flush = pipe.Writer.FlushAsync();
        await Assert.That(flush.IsCompletedSuccessfully).IsTrue();

        var read = await pipe.Reader.ReadAsync();
        var bytes = read.Buffer.ToArray();
        await Assert.That(bytes.Length).IsEqualTo(5);
        await Assert.That(bytes[0]).IsEqualTo((byte)1);
        await Assert.That(bytes[4]).IsEqualTo((byte)5);

        pipe.Reader.AdvanceTo(read.Buffer.End);
        pipe.Writer.Complete();
        var completed = await pipe.Reader.ReadAsync();
        await Assert.That(completed.IsCompleted).IsTrue();
        pipe.Reader.AdvanceTo(completed.Buffer.End);
        pipe.Reader.Complete();
    }

    [Test]
    public async Task CustomSchedulerResumesPendingReadsInOrder()
    {
        var scheduler = new CountingScheduler();
        var pipe = new Pipe(CreateOptions(scheduler, scheduler));
        var consumer = ConsumeOneAsync(pipe.Reader);

        await Assert.That(consumer.IsCompleted).IsFalse();

        var memory = pipe.Writer.GetMemory(1);
        memory.Span[0] = 42;
        pipe.Writer.Advance(1);
        var flush = pipe.Writer.FlushAsync();

        await Assert.That(flush.IsCompletedSuccessfully).IsTrue();
        await Assert.That(consumer.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await consumer).IsEqualTo(42);
        await Assert.That(scheduler.Count > 0).IsTrue();

        pipe.Writer.Complete();
        pipe.Reader.Complete();
    }

    [Test]
    public async Task PauseAndResumeThresholdsApplyBackpressure()
    {
        var pipe = new Pipe(CreateOptions(pause: 4, resume: 2));
        var memory = pipe.Writer.GetMemory(4);
        for (var index = 0; index < 4; index++)
        {
            memory.Span[index] = (byte)(10 + index);
        }

        pipe.Writer.Advance(4);
        var flush = pipe.Writer.FlushAsync();
        await Assert.That(flush.IsCompleted).IsFalse();

        var read = await pipe.Reader.ReadAsync();
        await Assert.That(read.Buffer.Length).IsEqualTo(4);
        pipe.Reader.AdvanceTo(read.Buffer.End);

        await Assert.That(flush.IsCompletedSuccessfully).IsTrue();
        await Assert.That(flush.Result.IsCanceled).IsFalse();

        pipe.Writer.Complete();
        pipe.Reader.Complete();
    }

    [Test]
    public async Task CanceledOperationsAndCompletionRemainObservable()
    {
        var pipe = new Pipe(CreateOptions());
        pipe.Reader.CancelPendingRead();
        var canceledRead = await pipe.Reader.ReadAsync();
        await Assert.That(canceledRead.IsCanceled).IsTrue();
        pipe.Reader.AdvanceTo(canceledRead.Buffer.Start);

        pipe.Writer.CancelPendingFlush();
        var canceledFlush = await pipe.Writer.FlushAsync();
        await Assert.That(canceledFlush.IsCanceled).IsTrue();

        pipe.Writer.Complete();
        var completed = await pipe.Reader.ReadAsync();
        await Assert.That(completed.IsCompleted).IsTrue();
        pipe.Reader.AdvanceTo(completed.Buffer.End);
        pipe.Reader.Complete();
    }

    [Test]
    public async Task StreamAdaptersReadAndWriteThroughPipes()
    {
        using var source = new MemoryStream([3, 5, 8]);
        var reader = PipeReader.Create(source, new StreamPipeReaderOptions(
            bufferSize: 2,
            minimumReadSize: 1,
            leaveOpen: true,
            useZeroByteReads: false));
        var read = await reader.ReadAsync();
        await Assert.That(read.Buffer.FirstSpan[0]).IsEqualTo((byte)3);
        reader.AdvanceTo(read.Buffer.End);
        reader.Complete();
        await Assert.That(source.CanRead).IsTrue();

        using var destination = new MemoryStream();
        var writer = PipeWriter.Create(destination, new StreamPipeWriterOptions(
            minimumBufferSize: 2,
            leaveOpen: true));
        var written = await writer.WriteAsync(new byte[] { 13, 21, 34 });
        await Assert.That(written.IsCanceled).IsFalse();
        writer.Complete();

        var bytes = destination.ToArray();
        await Assert.That(bytes.Length).IsEqualTo(3);
        await Assert.That(bytes[0]).IsEqualTo((byte)13);
        await Assert.That(bytes[2]).IsEqualTo((byte)34);
        await Assert.That(destination.CanRead).IsTrue();
    }

    [Test]
    public async Task SequencePipeReaderTraversesAndCompletesItsSequence()
    {
        var reader = PipeReader.Create(new ReadOnlySequence<byte>(new byte[] { 55, 89 }));
        await Assert.That(reader.TryRead(out var result)).IsTrue();
        await Assert.That(result.Buffer.Length).IsEqualTo(2);
        await Assert.That(result.Buffer.FirstSpan[1]).IsEqualTo((byte)89);

        reader.AdvanceTo(result.Buffer.End);
        var completed = await reader.ReadAsync();
        await Assert.That(completed.IsCompleted).IsTrue();
        await Assert.That(ReadOnlySequence<byte>.Empty.Equals(ReadOnlySequence<byte>.Empty)).IsTrue();
        reader.AdvanceTo(completed.Buffer.End);
        reader.Complete();
    }

    [Test]
    public async Task SequencePipeReaderConsumesCancellationBeforeReturningData()
    {
        var reader = PipeReader.Create(ReadOnlySequence<byte>.Empty);
        reader.CancelPendingRead();

        var canceled = await reader.ReadAsync();
        await Assert.That(canceled.IsCanceled).IsTrue();
        await Assert.That(canceled.IsCompleted).IsTrue();
        reader.AdvanceTo(canceled.Buffer.Start);

        var completed = await reader.ReadAsync();
        await Assert.That(completed.IsCanceled).IsFalse();
        await Assert.That(completed.IsCompleted).IsTrue();
        reader.AdvanceTo(completed.Buffer.End);
        reader.Complete();
    }

    [Test]
    public async Task DefaultSchedulingAndThreadPoolBoundaryRemainExplicit()
    {
        var options = new PipeOptions();
        var usesInlineScheduler = options.ReaderScheduler == PipeScheduler.Inline &&
            options.WriterScheduler == PipeScheduler.Inline;

        await Assert.That(options.ReaderScheduler is not null).IsTrue();
        await Assert.That(options.WriterScheduler is not null).IsTrue();

        Exception? threadPoolFailure = null;
        PipeScheduler? threadPool = null;
        try
        {
            threadPool = PipeScheduler.ThreadPool;
        }
        catch (PlatformNotSupportedException exception)
        {
            threadPoolFailure = exception;
        }

        if (usesInlineScheduler)
        {
            await Assert.That(threadPoolFailure is not null).IsTrue();
            await Assert.That(threadPool).IsNull();
        }
        else
        {
            await Assert.That(threadPool).IsNotNull();
            await Assert.That(threadPoolFailure).IsNull();
        }
    }

    [Test]
    public async Task CompletedPipeOperationsExposeDeterministicFailures()
    {
        var pipe = new Pipe(CreateOptions());
        pipe.Writer.Complete();

        Exception? writeFailure = null;
        try
        {
            _ = pipe.Writer.GetMemory(1);
        }
        catch (Exception exception)
        {
            writeFailure = exception;
        }

        await Assert.That(writeFailure is InvalidOperationException).IsTrue();
        await Assert.That(writeFailure!.Message.Length > 0).IsTrue();

        pipe.Reader.Complete();
        Exception? readFailure = null;
        try
        {
            _ = await pipe.Reader.ReadAsync();
        }
        catch (Exception exception)
        {
            readFailure = exception;
        }

        await Assert.That(readFailure is InvalidOperationException).IsTrue();
        await Assert.That(readFailure!.Message.Length > 0).IsTrue();
    }

    [Test]
    public async Task ReadAtLeastAsyncReturnsTheRequestedStructuredResult()
    {
        var pipe = new Pipe(CreateOptions());
        var written = await pipe.Writer.WriteAsync(new byte[] { 144, 233, 89 });
        await Assert.That(written.IsCanceled).IsFalse();
        pipe.Writer.Complete();

        var result = await pipe.Reader.ReadAtLeastAsync(3);
        var bytes = result.Buffer.ToArray();
        await Assert.That(bytes.Length).IsEqualTo(3);
        await Assert.That(bytes[0] + bytes[1] + bytes[2]).IsEqualTo(466);
        pipe.Reader.AdvanceTo(result.Buffer.End);
        pipe.Reader.Complete();
    }

    private static PipeOptions CreateOptions(
        PipeScheduler? reader = null,
        PipeScheduler? writer = null,
        long pause = 0,
        long resume = 0) => new(
            readerScheduler: reader ?? PipeScheduler.Inline,
            writerScheduler: writer ?? PipeScheduler.Inline,
            pauseWriterThreshold: pause,
            resumeWriterThreshold: resume,
            minimumSegmentSize: 2,
            useSynchronizationContext: false);

    private static async Task<int> ConsumeOneAsync(PipeReader reader)
    {
        var result = await reader.ReadAsync();
        var value = result.Buffer.FirstSpan[0];
        reader.AdvanceTo(result.Buffer.End);
        return value;
    }

    private sealed class CountingScheduler : PipeScheduler
    {
        public int Count { get; private set; }

        public override void Schedule(Action<object?> action, object? state)
        {
            Count++;
            action(state);
        }
    }
}
