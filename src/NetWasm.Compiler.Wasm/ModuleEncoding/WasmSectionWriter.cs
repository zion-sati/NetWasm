using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public sealed class WasmSectionWriter : IWasmSectionWriter
{
    private readonly IUnsignedLeb128Encoder _payloadLength;

    public WasmSectionWriter(IUnsignedLeb128Encoder payloadLength)
    {
        ArgumentNullException.ThrowIfNull(payloadLength);
        _payloadLength = payloadLength;
    }

    public void Write(IWasmBinaryWriter output, byte id, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write([id]);
        _payloadLength.Encode(output, (uint)payload.Length);
        output.Write(payload);
    }
}
