using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class SignedLeb12864InstructionEncoder : IWasmInstructionEncoder
{
    private readonly ISignedLeb12864Encoder _operandEncoder;

    public SignedLeb12864InstructionEncoder(ISignedLeb12864Encoder operandEncoder)
    {
        ArgumentNullException.ThrowIfNull(operandEncoder);
        _operandEncoder = operandEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        _operandEncoder.Encode(writer, instruction.Operand.Signed64Value);
    }
}
