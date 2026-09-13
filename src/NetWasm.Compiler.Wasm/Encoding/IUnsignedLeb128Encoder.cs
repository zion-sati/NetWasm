namespace NetWasm.Compiler.Wasm.Encoding;

public interface IUnsignedLeb128Encoder
{
    void Encode(IWasmBinaryWriter writer, uint value);
}
