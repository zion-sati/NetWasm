using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.LibraryProfileEdges;

public static class PipelineCases
{
    public static async Task SegmentsAndPartialConsumption()
    {
        var pipe = new Pipe(Options());
        var first = pipe.Writer.GetMemory(1);
        var length = first.Length;
        first.Span.Fill(7);
        pipe.Writer.Advance(length);
        await pipe.Writer.FlushAsync();
        var second = pipe.Writer.GetMemory(1);
        second.Span[0] = 9;
        pipe.Writer.Advance(1);
        await pipe.Writer.FlushAsync();
        pipe.Writer.Complete();
        var read = await pipe.Reader.ReadAsync();
        Program.Require(!read.Buffer.IsSingleSegment && read.Buffer.Length == length + 1 && read.IsCompleted);
        var bytes = read.Buffer.ToArray();
        for (var index = 0; index < length; index++) Program.Require(bytes[index] == 7);
        Program.Require(bytes[length] == 9);
        var consumed = read.Buffer.GetPosition(length);
        pipe.Reader.AdvanceTo(consumed, consumed);
        var remainder = await pipe.Reader.ReadAsync();
        Program.Require(remainder.Buffer.Length == 1 && remainder.Buffer.FirstSpan[0] == 9 && remainder.IsCompleted);
        pipe.Reader.AdvanceTo(remainder.Buffer.End);
        pipe.Reader.Complete();
    }

    public static async Task ResumeThreshold()
    {
        var pipe = new Pipe(Options(4, 2));
        var memory = pipe.Writer.GetMemory(4);
        memory.Span[..4].Fill(1);
        pipe.Writer.Advance(4);
        var flush = pipe.Writer.FlushAsync();
        Program.Require(!flush.IsCompleted);
        var read = await pipe.Reader.ReadAsync();
        var consumed = read.Buffer.GetPosition(2);
        pipe.Reader.AdvanceTo(consumed, consumed);
        Program.Require(!flush.IsCompleted);
        var remainder = await pipe.Reader.ReadAsync();
        Program.Require(remainder.Buffer.Length == 2);
        consumed = remainder.Buffer.GetPosition(1);
        pipe.Reader.AdvanceTo(consumed, consumed);
        Program.Require(flush.IsCompletedSuccessfully);
        var result = await flush;
        Program.Require(!result.IsCanceled && !result.IsCompleted);
        pipe.Writer.Complete();
        var last = await pipe.Reader.ReadAsync();
        Program.Require(last.Buffer.Length == 1 && last.IsCompleted);
        pipe.Reader.AdvanceTo(last.Buffer.End);
        pipe.Reader.Complete();
    }

    public static async Task PendingCancellationAndFailure()
    {
        var pipe = new Pipe(Options());
        var pending = pipe.Reader.ReadAsync();
        Program.Require(!pending.IsCompleted);
        pipe.Reader.CancelPendingRead();
        var canceled = await pending;
        Program.Require(canceled.IsCanceled && canceled.Buffer.IsEmpty && !canceled.IsCompleted);
        pipe.Reader.AdvanceTo(canceled.Buffer.Start);
        var next = pipe.Reader.ReadAsync();
        Program.Require(!next.IsCompleted);
        var failure = new InvalidOperationException("Planned writer completion failure.");
        pipe.Writer.Complete(failure);
        var caught = false;
        try { await next; }
        catch (InvalidOperationException observed) { caught = ReferenceEquals(failure, observed); }
        Program.Require(caught);
        pipe.Reader.Complete();
    }

    private static PipeOptions Options(long pause = 0, long resume = 0) => new(
        readerScheduler: PipeScheduler.Inline,
        writerScheduler: PipeScheduler.Inline,
        pauseWriterThreshold: pause,
        resumeWriterThreshold: resume,
        minimumSegmentSize: 16,
        useSynchronizationContext: false);
}
