using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm;

/// <summary>
/// Mutable byte storage shared by a raw writer and its snapshot reader.
/// </summary>
public sealed class WasmBinaryBuffer
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            _bytes.Add(value);
        }
    }

    internal byte[] Snapshot() => [.. _bytes];
}
