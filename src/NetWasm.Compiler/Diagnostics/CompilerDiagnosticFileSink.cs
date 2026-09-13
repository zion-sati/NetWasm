using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticFileSink(
    ICompilerDiagnosticLineFormatter formatter,
    ICompilerDiagnosticTextWriterFactory writerFactory,
    ICompilerDiagnosticClock clock) : ICompilerDiagnosticSink, IDisposable
{
    private readonly ICompilerDiagnosticLineFormatter _formatter =
        formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly ICompilerDiagnosticTextWriterFactory _writerFactory =
        writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
    private readonly ICompilerDiagnosticClock _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TextWriter> _writers = new(StringComparer.Ordinal);
    private long _sequence;
    private bool _disposed;

    public void Write(CompilerDiagnosticWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_writers.TryGetValue(write.Path, out var writer))
            {
                writer = _writerFactory.Create(write.Path);
                _writers.Add(write.Path, writer);
            }

            writer.WriteLine(_formatter.Format(++_sequence, _clock.Read(), write.Entry));
            writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }

            _writers.Clear();
            _disposed = true;
        }
    }
}
