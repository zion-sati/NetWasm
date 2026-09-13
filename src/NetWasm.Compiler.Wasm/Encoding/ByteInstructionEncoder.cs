using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class ByteInstructionEncoder : IWasmInstructionEncoder
{
    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode, instruction.Operand.ByteValue]);
    }
}
