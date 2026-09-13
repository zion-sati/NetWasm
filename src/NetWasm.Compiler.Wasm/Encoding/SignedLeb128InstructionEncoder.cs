using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class SignedLeb128InstructionEncoder : IWasmInstructionEncoder
{
    private readonly ISignedLeb128Encoder _operandEncoder;

    public SignedLeb128InstructionEncoder(ISignedLeb128Encoder operandEncoder)
    {
        ArgumentNullException.ThrowIfNull(operandEncoder);
        _operandEncoder = operandEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        _operandEncoder.Encode(writer, instruction.Operand.SignedValue);
    }
}
