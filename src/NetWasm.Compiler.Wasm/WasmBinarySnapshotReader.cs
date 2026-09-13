using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm;

/// <summary>
/// Snapshot reader for one mutable binary buffer.
/// </summary>
public sealed class WasmBinarySnapshotReader : IWasmBinarySnapshotReader
{
    private readonly WasmBinaryBuffer _buffer;

    public WasmBinarySnapshotReader(WasmBinaryBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    public byte[] Read() => _buffer.Snapshot();
}
