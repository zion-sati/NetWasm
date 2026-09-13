namespace NetWasm.Compiler.Wasm.Encoding;

public interface ISignedLeb128Encoder
{
    void Encode(IWasmBinaryWriter writer, int value);
}
