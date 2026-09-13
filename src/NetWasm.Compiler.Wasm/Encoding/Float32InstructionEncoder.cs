using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class Float32InstructionEncoder : IWasmInstructionEncoder
{
    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        writer.Write(BitConverter.GetBytes(instruction.Operand.Float32Value));
    }
}
