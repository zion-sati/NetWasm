// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticFileSinkTests
{
    [Fact]
    public void WriteReusesPathsAndPublishesOrderedFlushedLines()
    {
        var formatter = new RecordingFormatter();
        var writers = new RecordingWriterFactory();
        var timestamp = new DateTimeOffset(2026, 8, 25, 1, 2, 3, TimeSpan.Zero);
        var sink = Assert.IsAssignableFrom<ICompilerDiagnosticSink>(
            new CompilerDiagnosticFileSink(formatter, writers, new FixedClock(timestamp)));
        var entry = Entry();

        sink.Write(new CompilerDiagnosticWrite("a.jsonl", entry));
        sink.Write(new CompilerDiagnosticWrite("a.jsonl", entry));
        sink.Write(new CompilerDiagnosticWrite("b.jsonl", entry));

        Assert.Equal(["a.jsonl", "b.jsonl"], writers.Paths);
        Assert.Equal([1L, 2L, 3L], formatter.Sequences);
        Assert.All(formatter.Timestamps, value => Assert.Equal(timestamp, value));
        Assert.Equal(["1", "2"], writers.Writers["a.jsonl"].Lines);
        Assert.Equal(["3"], writers.Writers["b.jsonl"].Lines);
        Assert.Equal(2, writers.Writers["a.jsonl"].FlushCount);
        Assert.Equal(1, writers.Writers["b.jsonl"].FlushCount);
    }

    [Fact]
    public void DisposeClosesEveryWriterAndRejectsFurtherWrites()
    {
        var writers = new RecordingWriterFactory();
        var sink = new CompilerDiagnosticFileSink(
            new RecordingFormatter(),
            writers,
            new FixedClock(DateTimeOffset.UnixEpoch));
        sink.Write(new CompilerDiagnosticWrite("a.jsonl", Entry()));
        sink.Write(new CompilerDiagnosticWrite("b.jsonl", Entry()));

        sink.Dispose();
        sink.Dispose();

        Assert.All(writers.Writers.Values, writer => Assert.True(writer.Disposed));
        Assert.Throws<ObjectDisposedException>(() =>
            sink.Write(new CompilerDiagnosticWrite("c.jsonl", Entry())));
        Assert.DoesNotContain("c.jsonl", writers.Paths);
    }

    [Fact]
    public void WriteRejectsANullRequest()
    {
        var sink = Assert.IsAssignableFrom<ICompilerDiagnosticSink>(
            new CompilerDiagnosticFileSink(
                new RecordingFormatter(),
                new RecordingWriterFactory(),
                new FixedClock(DateTimeOffset.UnixEpoch)));

        Assert.Throws<ArgumentNullException>(() => sink.Write(null!));
    }

    private static CompilerDiagnosticEntry Entry()
        => new(
            "category",
            LogLevel.Information,
            new EventId(1, "event"),
            "message",
            ExceptionType: null,
            ExceptionMessage: null, null);

    private sealed class RecordingFormatter : ICompilerDiagnosticLineFormatter
    {
        public List<long> Sequences { get; } = [];

        public List<DateTimeOffset> Timestamps { get; } = [];

        public string Format(long sequence, DateTimeOffset timestampUtc, CompilerDiagnosticEntry entry)
        {
            Sequences.Add(sequence);
            Timestamps.Add(timestampUtc);
            return sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private sealed class RecordingWriterFactory : ICompilerDiagnosticTextWriterFactory
    {
        public List<string> Paths { get; } = [];

        public Dictionary<string, RecordingTextWriter> Writers { get; } = [];

        public TextWriter Create(string path)
        {
            Paths.Add(path);
            var writer = new RecordingTextWriter();
            Writers.Add(path, writer);
            return writer;
        }
    }

    private sealed class FixedClock(DateTimeOffset timestamp) : ICompilerDiagnosticClock
    {
        public DateTimeOffset Read() => timestamp;
    }

    private sealed class RecordingTextWriter : StringWriter
    {
        public List<string> Lines { get; } = [];

        public int FlushCount { get; private set; }

        public bool Disposed { get; private set; }

        public override void WriteLine(string? value) => Lines.Add(value ?? string.Empty);

        public override void Flush() => FlushCount++;

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
