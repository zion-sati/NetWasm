// From the compiler harness's StreamApmContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.IO;
using System.Threading.Tasks;
namespace ApmFixture;

public static class EntryPoint
{
    static string Failure(Action action)
    {
        try { action(); return "none"; }
        // Named by type test, as a program carries no type names (issue 225).
        catch (Exception ex)
        {
            return ex switch
            {
                InvalidOperationException => nameof(InvalidOperationException),
                ArgumentNullException => nameof(ArgumentNullException),
                ArgumentException => nameof(ArgumentException),
                NotSupportedException => nameof(NotSupportedException),
                _ => "an exception the fixture does not name",
            };
        }
    }

    public static int Run(int input)
    {
        // MemoryStream inherits Stream's APM: a write then a read, each carrying its state.
        var stream = new MemoryStream();
        var state = new object();
        var calls = 0;
        IAsyncResult write = stream.BeginWrite(new byte[] { 42, 7 }, 0, 2, r => { if (ReferenceEquals(r.AsyncState, state)) calls++; }, state);
        if (!ReferenceEquals(write.AsyncState, state)) return 1;
        stream.EndWrite(write);
        if (stream.Length != 2) return 2;
        stream.Position = 0;
        var buffer = new byte[4];
        IAsyncResult read = stream.BeginRead(buffer, 0, 4, r => { if (ReferenceEquals(r.AsyncState, state)) calls++; }, state);
        if (stream.EndRead(read) != 2 || buffer[0] != 42 || buffer[1] != 7) return 3;
#if !DESKTOP_ORACLE
        // The callbacks ran when the inlined read and write completed.
        if (calls != 2) return 4;
#endif

        // Upstream's validation: End twice, the wrong End, a foreign IAsyncResult, null.
        if (Failure(() => stream.EndRead(read)) != nameof(InvalidOperationException)) return 5;
        if (Failure(() => stream.EndWrite(read)) != nameof(ArgumentException)) return 6;
        if (Failure(() => stream.EndRead(new Result())) != nameof(ArgumentException)) return 7;
        if (Failure(() => stream.EndRead(null!)) != nameof(ArgumentNullException)) return 8;
        if (Failure(() => new Plain(canRead: false).BeginRead(buffer, 0, 1, null, null)) != nameof(NotSupportedException)) return 9;

        // A stream with only Read and Write: ReadAsync and WriteAsync take the ReadWriteTask shortcut, so
        // synchronous waits on them complete.
        var plain = new Plain(canRead: true);
        if (plain.ReadAsync(buffer, 0, 4).GetAwaiter().GetResult() != 3 || buffer[0] != 9) return 10;
        plain.WriteAsync(buffer, 0, 1).GetAwaiter().GetResult();
        if (plain.Written != 1) return 11;

        // A stream with its own Begin/End: ReadAsync goes through them.
        var custom = new Custom();
        if (custom.ReadAsync(buffer, 0, 1).GetAwaiter().GetResult() != 42 || custom.BeginCalls != 1) return 12;

        // BufferedStream's APM.
        var buffered = new BufferedStream(new MemoryStream(new byte[] { 5, 6, 7 }));
        var bufferedRead = buffered.BeginRead(buffer, 0, 3, null, null);
        if (buffered.EndRead(bufferedRead) != 3 || buffer[2] != 7) return 13;
        return input + 1;
    }
}

public sealed class Result(object state = null) : IAsyncResult
{
    public object AsyncState => state;
    public System.Threading.WaitHandle AsyncWaitHandle => throw new NotSupportedException();
    public bool CompletedSynchronously => true;
    public bool IsCompleted => true;
}

public sealed class Plain(bool canRead) : Stream
{
    public int Written;
    public override bool CanRead => canRead;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) { buffer[offset] = 9; return 3; }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => Written += count;
}

public sealed class Custom : Stream
{
    public int BeginCalls;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Read is not used");
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        BeginCalls++;
        var result = new Result(state);
        callback?.Invoke(result);
        return result;
    }
    public override int EndRead(IAsyncResult result) => 42;
}
