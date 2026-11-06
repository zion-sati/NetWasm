using System;

namespace NetWasm.Compiler.Wasm.Encoding;

// Encodes a SIMD lane load or store: the prefix, the subopcode, the memory
// argument's alignment and offset, each as unsigned LEB128, then the lane byte.
public sealed class PrefixedMemoryLaneInstructionEncoder : IWasmInstructionEncoder
{
    private readonly IUnsignedLeb128Encoder _operandEncoder;

    public PrefixedMemoryLaneInstructionEncoder(IUnsignedLeb128Encoder operandEncoder)
    {
        ArgumentNullException.ThrowIfNull(operandEncoder);
        _operandEncoder = operandEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        _operandEncoder.Encode(writer, instruction.Operand.Alignment);
        _operandEncoder.Encode(writer, instruction.Operand.Offset);
        _operandEncoder.Encode(writer, instruction.Operand.Third);
        writer.Write([instruction.Operand.ByteValue]);
    }
}
