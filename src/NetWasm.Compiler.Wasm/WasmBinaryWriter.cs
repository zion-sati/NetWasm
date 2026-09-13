using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm;

/// <summary>
/// Raw byte sink used by binary encoding actors.
/// </summary>
public sealed class WasmBinaryWriter : IWasmBinaryWriter
{
    private readonly WasmBinaryBuffer _buffer;

    public WasmBinaryWriter(WasmBinaryBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    public void Write(ReadOnlySpan<byte> bytes) => _buffer.Append(bytes);
}
