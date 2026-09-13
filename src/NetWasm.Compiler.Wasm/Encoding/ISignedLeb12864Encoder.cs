namespace NetWasm.Compiler.Wasm.Encoding;

public interface ISignedLeb12864Encoder
{
    void Encode(IWasmBinaryWriter writer, long value);
}
