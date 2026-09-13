namespace NetWasm.Compiler.Wasm.Encoding;

public interface IUtf8StringEncoder
{
    void Encode(IWasmBinaryWriter writer, string value);
}
