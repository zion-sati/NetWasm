using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class UnsignedLeb128Encoder : IUnsignedLeb128Encoder
{
    public void Encode(IWasmBinaryWriter writer, uint value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            writer.Write([current]);
        } while (value != 0);
    }
}
