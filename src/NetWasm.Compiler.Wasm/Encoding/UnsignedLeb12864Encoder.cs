using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class UnsignedLeb12864Encoder : IUnsignedLeb12864Encoder
{
    public void Encode(IWasmBinaryWriter writer, ulong value)
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
