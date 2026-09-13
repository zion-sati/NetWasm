namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Encodes one operand shape of a WebAssembly instruction.
/// </summary>
public interface IWasmInstructionEncoder
{
    void Encode(IWasmBinaryWriter writer, WasmInstruction instruction);
}
