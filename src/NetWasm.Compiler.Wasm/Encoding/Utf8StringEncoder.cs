using System;
using System.Text;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class Utf8StringEncoder : IUtf8StringEncoder
{
    private readonly IUnsignedLeb128Encoder _lengthEncoder;

    public Utf8StringEncoder(IUnsignedLeb128Encoder lengthEncoder)
    {
        ArgumentNullException.ThrowIfNull(lengthEncoder);
        _lengthEncoder = lengthEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, string value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        _lengthEncoder.Encode(writer, (uint)bytes.Length);
        writer.Write(bytes);
    }
}
