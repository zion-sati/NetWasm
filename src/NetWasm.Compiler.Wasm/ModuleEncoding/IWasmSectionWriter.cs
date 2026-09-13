using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public interface IWasmSectionWriter
{
    void Write(IWasmBinaryWriter output, byte id, ReadOnlySpan<byte> payload);
}
