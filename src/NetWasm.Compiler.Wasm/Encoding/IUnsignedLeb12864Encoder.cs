namespace NetWasm.Compiler.Wasm.Encoding;

public interface IUnsignedLeb12864Encoder
{
    void Encode(IWasmBinaryWriter writer, ulong value);
}
