using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class UnsignedLeb12864InstructionEncoder : IWasmInstructionEncoder
{
    private readonly IUnsignedLeb12864Encoder _operandEncoder;

    public UnsignedLeb12864InstructionEncoder(IUnsignedLeb12864Encoder operandEncoder)
    {
        ArgumentNullException.ThrowIfNull(operandEncoder);
        _operandEncoder = operandEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        _operandEncoder.Encode(writer, instruction.Operand.Unsigned64Value);
    }
}
