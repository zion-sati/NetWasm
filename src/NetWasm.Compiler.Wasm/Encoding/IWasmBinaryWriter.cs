using System;

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Raw byte sink used by encoding Strategies.
/// </summary>
public interface IWasmBinaryWriter
{
    void Write(ReadOnlySpan<byte> bytes);
}
