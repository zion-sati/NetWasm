using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class PrefixedUnsignedLeb128InstructionEncoder : IWasmInstructionEncoder
{
    private readonly IUnsignedLeb128Encoder _operandEncoder;

    public PrefixedUnsignedLeb128InstructionEncoder(IUnsignedLeb128Encoder operandEncoder)
    {
        ArgumentNullException.ThrowIfNull(operandEncoder);
        _operandEncoder = operandEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        _operandEncoder.Encode(writer, instruction.Operand.UnsignedValue);
    }
}
