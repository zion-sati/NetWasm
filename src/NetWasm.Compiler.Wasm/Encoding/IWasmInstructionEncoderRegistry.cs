namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Resolves an instruction encoding Strategy by its immutable operand shape.
/// </summary>
public interface IWasmInstructionEncoderRegistry
{
    IWasmInstructionEncoder Resolve(WasmInstructionOperandShape shape);
}
