using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// An append-only buffer of chunks, so a segment's section grows without one array that must hold
/// it all (an array cannot pass 2 GB, issue 229) and without copying what it holds as it grows. A
/// span it hands out is contiguous within one chunk; its logical offsets count the elements written.
/// </summary>
internal sealed class UnitChunkedBuffer<T>(int chunkLength) where T : unmanaged
{
    private readonly List<(T[] Chunk, int Written)> _chunks = [];
    private T[] _current = [];
    private int _written;

    internal long WrittenCount { get; private set; }

    // A contiguous span of at least length elements at the end, which Advance then claims.
    internal Span<T> GetSpan(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (_current.Length - _written < length)
        {
            if (_written != 0)
            {
                _chunks.Add((_current, _written));
            }
            _current = new T[Math.Max(chunkLength, length)];
            _written = 0;
        }
        return _current.AsSpan(_written, length);
    }

    internal void Advance(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _current.Length - _written);
        _written += length;
        WrittenCount += length;
    }

    // Appends values, across chunks when they do not fit in the current one.
    internal void Write(ReadOnlySpan<T> values)
    {
        while (!values.IsEmpty)
        {
            if (_current.Length == _written)
            {
                GetSpan(Math.Min(values.Length, chunkLength));
            }
            var count = Math.Min(values.Length, _current.Length - _written);
            values[..count].CopyTo(_current.AsSpan(_written));
            Advance(count);
            values = values[count..];
        }
    }

    // The written elements, chunk by chunk, in order.
    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var (chunk, written) in _chunks)
        {
            destination.Write(MemoryMarshal.AsBytes(chunk.AsSpan(0, written)));
        }
        destination.Write(MemoryMarshal.AsBytes(_current.AsSpan(0, _written)));
    }
}
