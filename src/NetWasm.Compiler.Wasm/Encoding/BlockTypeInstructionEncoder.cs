using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class BlockTypeInstructionEncoder : IWasmInstructionEncoder
{
    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode, instruction.Operand.BlockTypeValue]);
    }
}
