using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AsyncIoSemanticsCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void BaseAndInMemoryIoPreserveAsyncOrdering(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.IO;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncIoSemanticsFixture;

            public static class EntryPoint
            {
                private static readonly AsyncLocal<int> Local = new();
                private static Task<int>? _read;
                private static Task? _write;
                private static Task<int>? _operationCanceledFault;
                private static Task<int>? _failedRead;
                private static Task? _writeAfterFailure;
                private static ProbeStream? _probe;
                private static FailingOnceStream? _failing;
                private static int _failure;

                public static int Run(int input)
                {
                    _failure = 0;
                    CheckImmediateStringIo();

                    _probe = new ProbeStream();
                    Local.Value = 11;
                    _read = _probe.ReadAsync(new byte[1], 0, 1);
                    using var cancellation = new CancellationTokenSource();
                    Local.Value = 22;
                    _write = _probe.WriteAsync(
                        new byte[] { 1 },
                        0,
                        1,
                        cancellation.Token);
                    cancellation.Cancel();
                    Local.Value = 0;

                    _operationCanceledFault = new OperationCanceledStream()
                        .ReadAsync(new byte[1], 0, 1);

                    _failing = new FailingOnceStream();
                    _failedRead = _failing.ReadAsync(new byte[1], 0, 1);
                    _writeAfterFailure = _failing.WriteAsync(
                        new byte[] { 1 }, 0, 1);
                    return _failure;
                }

                public static int Observe(int input)
                {
                    Check(_read is { IsCompletedSuccessfully: true } &&
                        _read.Result == 1, 20);
                    Check(_write is { IsCompletedSuccessfully: true }, 21);
                    Check(_probe is not null &&
                        _probe.Count == 2 &&
                        _probe.Operations[0] == 1 &&
                        _probe.Operations[1] == 2 &&
                        _probe.Contexts[0] == 11 &&
                        _probe.Contexts[1] == 22, 22);
                    Check(_operationCanceledFault is
                        { IsFaulted: true, IsCanceled: false }, 23);
                    Check(_failedRead is { IsFaulted: true } &&
                        _writeAfterFailure is { IsCompletedSuccessfully: true } &&
                        _failing is { WriteCount: 1 }, 24);
                    return _failure == 0 ? input + 1 : _failure;
                }

                private static void CheckImmediateStringIo()
                {
                    using var reader = new StringReader("abc");
                    var chars = new char[3];
                    var read = reader.ReadAsync(chars, 0, 2);
                    Check(read.IsCompletedSuccessfully &&
                        read.Result == 2 &&
                        chars[0] == 'a' && chars[1] == 'b', 1);

                    var memoryRead = reader.ReadAsync(
                        chars.AsMemory(2, 1),
                        CancellationToken.None);
                    Check(memoryRead.IsCompletedSuccessfully &&
                        memoryRead.Result == 1 && chars[2] == 'c', 2);

                    using var canceled = new CancellationTokenSource();
                    canceled.Cancel();
                    var canceledRead = reader.ReadAsync(
                        chars.AsMemory(0, 1), canceled.Token);
                    Check(canceledRead.IsCanceled, 3);

                    using var writer = new StringWriter();
                    var write = writer.WriteAsync("ab");
                    Check(write.IsCompletedSuccessfully &&
                        writer.ToString() == "ab", 4);
                    var line = writer.WriteLineAsync("c".AsMemory());
                    Check(line.IsCompletedSuccessfully &&
                        writer.ToString() == "abc\n", 5);
                    var canceledWrite = writer.WriteAsync(
                        "x".AsMemory(), canceled.Token);
                    Check(canceledWrite.IsCanceled &&
                        writer.ToString() == "abc\n", 6);
                }

                private static void Check(bool condition, int code)
                {
                    if (!condition && _failure == 0)
                    {
                        _failure = code;
                    }
                }

                private sealed class ProbeStream : Stream
                {
                    public int Count;
                    public readonly int[] Operations = new int[2];
                    public readonly int[] Contexts = new int[2];
                    public override bool CanRead => true;
                    public override bool CanSeek => false;
                    public override bool CanWrite => true;
                    public override long Length => 0;
                    public override long Position { get => 0; set => throw new NotSupportedException(); }
                    public override void Flush() { }
                    public override int Read(byte[] buffer, int offset, int count)
                    {
                        Record(1);
                        return 1;
                    }
                    public override void Write(byte[] buffer, int offset, int count) => Record(2);
                    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                    public override void SetLength(long value) => throw new NotSupportedException();
                    private void Record(int operation)
                    {
                        var index = Count++;
                        Operations[index] = operation;
                        Contexts[index] = Local.Value;
                    }
                }

                private sealed class OperationCanceledStream : Stream
                {
                    public override bool CanRead => true;
                    public override bool CanSeek => false;
                    public override bool CanWrite => false;
                    public override long Length => 0;
                    public override long Position { get => 0; set => throw new NotSupportedException(); }
                    public override void Flush() { }
                    public override int Read(byte[] buffer, int offset, int count) =>
                        throw new OperationCanceledException();
                    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
                    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                    public override void SetLength(long value) => throw new NotSupportedException();
                }

                private sealed class FailingOnceStream : Stream
                {
                    public int WriteCount;
                    public override bool CanRead => true;
                    public override bool CanSeek => false;
                    public override bool CanWrite => true;
                    public override long Length => 0;
                    public override long Position { get => 0; set => throw new NotSupportedException(); }
                    public override void Flush() { }
                    public override int Read(byte[] buffer, int offset, int count) =>
                        throw new InvalidOperationException();
                    public override void Write(byte[] buffer, int offset, int count) => WriteCount++;
                    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                    public override void SetLength(long value) => throw new NotSupportedException();
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "AsyncIoSemanticsFixture",
            source,
            "AsyncIoSemanticsFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Exports =
            [
                new RequestedExport(
                    "run",
                    "AsyncIoSemanticsFixture.EntryPoint",
                    "Run"),
                new RequestedExport(
                    "observe",
                    "AsyncIoSemanticsFixture.EntryPoint",
                    "Observe"),
            ],
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            DrainReactor = true,
            ObserveExportName = "observe",
        });

        Assert.Equal(42, observed);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void StreamBackedReadersAndWritersUseTrueAsyncPaths(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.IO;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncStreamIoFixture;

            public static class EntryPoint
            {
                private static Task<string?>? _line;
                private static Task? _write;
                private static StreamReader? _reader;
                private static StreamWriter? _writer;
                private static AsyncOnlyStream? _readStream;
                private static AsyncOnlyStream? _writeStream;
                private static AsyncOnlyStream? _bufferedReadStream;
                private static AsyncOnlyStream? _bufferedWriteStream;
                private static AsyncOnlyStream? _copySourceStream;
                private static AsyncOnlyStream? _copyDestinationStream;
                private static Task? _bufferedRead;
                private static Task? _bufferedWrite;
                private static Task? _copy;
                private static int _failure;

                public static int Run(int input)
                {
                    _failure = 0;
                    _readStream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("alpha\nbeta"));
                    _reader = new StreamReader(_readStream, Encoding.UTF8, true, 128, true);
                    _line = _reader.ReadLineAsync();
                    Check(Rejects(() => _reader.Read()), 1);

                    _writeStream = new AsyncOnlyStream(Array.Empty<byte>());
                    _writer = new StreamWriter(
                        _writeStream,
                        new UTF8Encoding(false, true),
                        128,
                        true);
                    _write = WriteAllAsync(_writer, new string('x', 200));
                    Check(Rejects(() => _writer.Write('y')), 2);

                    _bufferedReadStream = new AsyncOnlyStream(
                        new byte[] { 1, 2, 3, 4, 5, 6 });
                    _bufferedRead = ReadBufferedAsync(
                        new BufferedStream(_bufferedReadStream, 4));
                    _bufferedWriteStream = new AsyncOnlyStream(Array.Empty<byte>());
                    _bufferedWrite = WriteBufferedAsync(
                        new BufferedStream(_bufferedWriteStream, 4));
                    _copySourceStream = new AsyncOnlyStream(
                        new byte[] { 10, 11, 12, 13, 14, 15 });
                    _copyDestinationStream = new AsyncOnlyStream(Array.Empty<byte>());
                    _copy = CopyBufferedAsync(
                        new BufferedStream(_copySourceStream, 4),
                        _copyDestinationStream);
                    return _failure;
                }

                public static int Observe(int input)
                {
                    Check(_line is { IsCompletedSuccessfully: true } &&
                        _line.Result == "alpha", 10);
                    Check(_readStream is { AsyncReadCount: > 0, SyncReadCount: 0 }, 11);
                    Check(_write is { IsCompletedSuccessfully: true }, 12);
                    Check(_writeStream is
                    {
                        AsyncWriteCount: >= 2,
                        AsyncFlushCount: 1,
                        SyncWriteCount: 0,
                        OutputCount: 200,
                    }, 13);
                    Check(_bufferedRead is { IsCompletedSuccessfully: true } &&
                        _bufferedReadStream is
                        {
                            AsyncReadCount: 2,
                            SyncReadCount: 0,
                        }, 14);
                    Check(_bufferedWrite is { IsCompletedSuccessfully: true } &&
                        _bufferedWriteStream is
                        {
                            AsyncWriteCount: 2,
                            AsyncFlushCount: 1,
                            SyncWriteCount: 0,
                            OutputCount: 6,
                            DisposeCount: 1,
                        }, 15);
                    Check(_copy is { IsCompletedSuccessfully: true } &&
                        _copyDestinationStream is
                        {
                            AsyncWriteCount: 2,
                            SyncWriteCount: 0,
                            OutputCount: 5,
                        }, 16);

                    try
                    {
                        _writer!.Write('z');
                    }
                    catch
                    {
                        Check(false, 17);
                    }
                    return _failure == 0 ? input + 1 : _failure;
                }

                private static async Task WriteAllAsync(StreamWriter writer, string value)
                {
                    await writer.WriteAsync(value).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                private static async Task ReadBufferedAsync(BufferedStream stream)
                {
                    var bytes = new byte[6];
                    var first = stream.ReadAsync(bytes.AsMemory(0, 3));
                    var second = stream.ReadAsync(bytes.AsMemory(3, 3));
                    var firstCount = await first.ConfigureAwait(false);
                    var secondCount = await second.ConfigureAwait(false);
                    Check(firstCount == 3 && secondCount == 3 &&
                        bytes[0] == 1 && bytes[1] == 2 && bytes[2] == 3 &&
                        bytes[3] == 4 && bytes[4] == 5 && bytes[5] == 6, 3);
                }

                private static async Task WriteBufferedAsync(BufferedStream stream)
                {
                    await stream.WriteAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
                    await stream.WriteAsync(new byte[] { 4, 5, 6 }).ConfigureAwait(false);
                    await stream.DisposeAsync().ConfigureAwait(false);
                }

                private static async Task CopyBufferedAsync(
                    BufferedStream source,
                    Stream destination)
                {
                    var first = new byte[1];
                    var count = await source.ReadAsync(first).ConfigureAwait(false);
                    Check(count == 1 && first[0] == 10, 4);
                    await source.CopyToAsync(destination, 2).ConfigureAwait(false);
                }

                private static bool Rejects(Action action)
                {
                    try
                    {
                        action();
                        return false;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }

                private static void Check(bool condition, int code)
                {
                    if (!condition && _failure == 0)
                        _failure = code;
                }

                private sealed class AsyncOnlyStream : Stream
                {
                    private readonly byte[] _input;
                    private readonly byte[] _output = new byte[512];
                    private int _inputPosition;
                    public int AsyncReadCount;
                    public int AsyncWriteCount;
                    public int AsyncFlushCount;
                    public int SyncReadCount;
                    public int SyncWriteCount;
                    public int OutputCount;
                    public int DisposeCount;

                    public AsyncOnlyStream(byte[] input) => _input = input;
                    public override bool CanRead => true;
                    public override bool CanSeek => false;
                    public override bool CanWrite => true;
                    public override long Length => 0;
                    public override long Position { get => 0; set => throw new NotSupportedException(); }
                    public override void Flush() => throw new InvalidOperationException("sync flush");
                    public override int Read(byte[] buffer, int offset, int count)
                    {
                        SyncReadCount++;
                        throw new InvalidOperationException("sync read");
                    }
                    public override void Write(byte[] buffer, int offset, int count)
                    {
                        SyncWriteCount++;
                        throw new InvalidOperationException("sync write");
                    }
                    public override async ValueTask<int> ReadAsync(
                        Memory<byte> buffer,
                        CancellationToken cancellationToken = default)
                    {
                        await Task.Yield();
                        cancellationToken.ThrowIfCancellationRequested();
                        AsyncReadCount++;
                        var count = Math.Min(buffer.Length, _input.Length - _inputPosition);
                        _input.AsSpan(_inputPosition, count).CopyTo(buffer.Span);
                        _inputPosition += count;
                        return count;
                    }
                    public override async ValueTask WriteAsync(
                        ReadOnlyMemory<byte> buffer,
                        CancellationToken cancellationToken = default)
                    {
                        await Task.Yield();
                        cancellationToken.ThrowIfCancellationRequested();
                        AsyncWriteCount++;
                        buffer.Span.CopyTo(_output.AsSpan(OutputCount));
                        OutputCount += buffer.Length;
                    }
                    public override async Task FlushAsync(CancellationToken cancellationToken)
                    {
                        await Task.Yield();
                        cancellationToken.ThrowIfCancellationRequested();
                        AsyncFlushCount++;
                    }
                    public override async ValueTask DisposeAsync()
                    {
                        await Task.Yield();
                        DisposeCount++;
                    }
                    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                    public override void SetLength(long value) => throw new NotSupportedException();
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "AsyncStreamIoFixture",
            source,
            "AsyncStreamIoFixture.EntryPoint",
            optimize,
            target,
            77,
            [])
        {
            Exports =
            [
                new RequestedExport("run", "AsyncStreamIoFixture.EntryPoint", "Run"),
                new RequestedExport("observe", "AsyncStreamIoFixture.EntryPoint", "Observe"),
            ],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            DrainReactor = true,
            ObserveExportName = "observe",
        });

        Assert.Equal(78, observed);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void StreamWriterPreservesLeaveOpenAndDerivedAsyncDispatch(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.IO;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;

            namespace StreamWriterFidelityFixture;

            public static class EntryPoint
            {
                private static int _failure;
                private static MemoryStream? _asyncStream;
                private static StreamWriter? _asyncWriter;
                private static Task? _asyncDispose;
                private static DerivedWriteLineWriter? _derivedLines;
                private static Task? _emptyLine;
                private static Task? _textLine;
                private static Task? _nullLine;
                private static AsyncFlushWriter? _asyncFlush;
                private static Task? _flush;
                private static SyncFlushWriter? _syncFlush;
                private static Task? _firstSyncFlush;
                private static Task? _secondSyncFlush;
                private static AsyncDisposeProbe? _synchronizedProbe;
                private static Task? _synchronizedDispose;

                public static int Run(int input)
                {
                    _failure = 0;
                    var syncStream = new MemoryStream();
                    var syncWriter = new StreamWriter(
                        syncStream, new UTF8Encoding(false), 128, true);
                    syncWriter.Write("A");
                    syncWriter.Dispose();
                    syncWriter.Write("B");
                    syncWriter.Flush();
                    Check(Encoding.UTF8.GetString(syncStream.ToArray()) == "AB" &&
                        syncStream.CanWrite, 1);

                    _asyncStream = new MemoryStream();
                    _asyncWriter = new StreamWriter(
                        _asyncStream, new UTF8Encoding(false), 128, true);
                    _asyncWriter.Write("C");
                    _asyncDispose = _asyncWriter.DisposeAsync().AsTask();

                    _derivedLines = new DerivedWriteLineWriter();
                    _emptyLine = _derivedLines.WriteLineAsync();
                    _textLine = _derivedLines.WriteLineAsync("x");
                    _nullLine = _derivedLines.WriteLineAsync((string?)null);

                    _asyncFlush = new AsyncFlushWriter();
                    _flush = _asyncFlush.FlushAsync(CancellationToken.None);
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    var canceled = _asyncFlush.FlushAsync(cancellation.Token);
                    Check(canceled.IsCanceled && _asyncFlush.Count == 1, 2);

                    _syncFlush = new SyncFlushWriter();
                    _firstSyncFlush = _syncFlush.FlushAsync();
                    _secondSyncFlush = _syncFlush.FlushAsync(CancellationToken.None);

                    StreamWriter.Null.Write(new ThrowingValue());
                    StreamWriter.Null.Write("{0", new ThrowingValue());
                    StreamWriter.Null.WriteLine(new ThrowingValue());
                    StreamWriter.Null.WriteLine("{0", new ThrowingValue());
                    Check(StreamWriter.Null.FlushAsync(cancellation.Token).IsCompletedSuccessfully &&
                        StreamWriter.Null.DisposeAsync().IsCompletedSuccessfully, 3);

                    var firstBroadcast = new StringWriter();
                    var secondBroadcast = new StringWriter();
                    var broadcasting = TextWriter.CreateBroadcasting(firstBroadcast, secondBroadcast);
                    var broadcastWrite = broadcasting.WriteLineAsync("q");
                    Check(broadcastWrite.IsCompletedSuccessfully &&
                        firstBroadcast.ToString() == "q\n" &&
                        secondBroadcast.ToString() == "q\n" &&
                        broadcasting.DisposeAsync().IsCompletedSuccessfully, 4);

                    _synchronizedProbe = new AsyncDisposeProbe();
                    _synchronizedDispose = Stream.Synchronized(_synchronizedProbe)
                        .DisposeAsync().AsTask();
                    return _failure;
                }

                public static int Observe(int input)
                {
                    Check(_asyncDispose is { IsCompletedSuccessfully: true }, 10);
                    _asyncWriter!.Write("D");
                    _asyncWriter.Flush();
                    Check(Encoding.UTF8.GetString(_asyncStream!.ToArray()) == "CD" &&
                        _asyncStream.CanWrite, 11);
                    Check(_emptyLine is { IsCompletedSuccessfully: true } &&
                        _textLine is { IsCompletedSuccessfully: true } &&
                        _nullLine is { IsCompletedSuccessfully: true } &&
                        _derivedLines is { ArrayWrites: 2, StringLines: 1 }, 12);
                    Check(_flush is { IsCompletedSuccessfully: true } &&
                        _asyncFlush is { Count: 1 }, 13);
                    Check(_firstSyncFlush is { IsCompletedSuccessfully: true } &&
                        _secondSyncFlush is { IsCompletedSuccessfully: true } &&
                        _syncFlush is { Count: 2 }, 14);
                    Check(_synchronizedDispose is { IsCompletedSuccessfully: true } &&
                        _synchronizedProbe is { AsyncDisposeCount: 1, SyncDisposeCount: 0 }, 15);
                    return _failure == 0 ? input + 1 : _failure;
                }

                private static void Check(bool condition, int failure)
                {
                    if (!condition && _failure == 0)
                        _failure = failure;
                }

                private sealed class DerivedWriteLineWriter : StreamWriter
                {
                    public int ArrayWrites;
                    public int StringLines;
                    public DerivedWriteLineWriter()
                        : base(new MemoryStream(), new UTF8Encoding(false), 128, true) { }
                    public override void Write(char[] buffer, int index, int count) => ArrayWrites++;
                    public override void WriteLine(string? value) => StringLines++;
                }

                private sealed class AsyncFlushWriter : StreamWriter
                {
                    public int Count;
                    public AsyncFlushWriter()
                        : base(new MemoryStream(), new UTF8Encoding(false), 128, true) { }
                    public override Task FlushAsync()
                    {
                        Count++;
                        return Task.CompletedTask;
                    }
                }

                private sealed class SyncFlushWriter : StreamWriter
                {
                    public int Count;
                    public SyncFlushWriter()
                        : base(new MemoryStream(), new UTF8Encoding(false), 128, true) { }
                    public override void Flush() => Count++;
                }

                private sealed class ThrowingValue
                {
                    public override string ToString() => throw new InvalidOperationException();
                }

                private sealed class AsyncDisposeProbe : Stream
                {
                    public int AsyncDisposeCount;
                    public int SyncDisposeCount;
                    public override bool CanRead => false;
                    public override bool CanSeek => false;
                    public override bool CanWrite => false;
                    public override long Length => 0;
                    public override long Position { get => 0; set => throw new NotSupportedException(); }
                    public override void Flush() { }
                    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
                    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                    public override void SetLength(long value) => throw new NotSupportedException();
                    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
                    protected override void Dispose(bool disposing)
                    {
                        if (disposing)
                            SyncDisposeCount++;
                    }
                    public override async ValueTask DisposeAsync()
                    {
                        await Task.Yield();
                        AsyncDisposeCount++;
                    }
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"StreamWriterFidelity_{target}_{optimize}",
            source,
            "StreamWriterFidelityFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Exports =
            [
                new RequestedExport("run", "StreamWriterFidelityFixture.EntryPoint", "Run"),
                new RequestedExport("observe", "StreamWriterFidelityFixture.EntryPoint", "Observe"),
            ],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            DrainReactor = true,
            ObserveExportName = "observe",
        });

        Assert.Equal(42, observed);
    }
}
