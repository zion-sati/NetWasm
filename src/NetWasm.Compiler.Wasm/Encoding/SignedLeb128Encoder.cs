using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class SignedLeb128Encoder : ISignedLeb128Encoder
{
    public void Encode(IWasmBinaryWriter writer, int value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        bool more;
        do
        {
            var current = (byte)(value & 0x7f);
            value >>= 7;
            var signBit = (current & 0x40) != 0;
            more = !((value == 0 && !signBit) || (value == -1 && signBit));
            if (more)
            {
                current |= 0x80;
            }

            writer.Write([current]);
        } while (more);
    }
}
