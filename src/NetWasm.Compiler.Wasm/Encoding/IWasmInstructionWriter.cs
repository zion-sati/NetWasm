namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Writes immutable WebAssembly instruction Commands.
/// </summary>
public interface IWasmInstructionWriter
{
    void Write(WasmInstruction instruction);
}
