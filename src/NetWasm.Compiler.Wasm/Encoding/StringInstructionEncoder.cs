using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class StringInstructionEncoder : IWasmInstructionEncoder
{
    private readonly IUtf8StringEncoder _stringEncoder;

    public StringInstructionEncoder(IUtf8StringEncoder stringEncoder)
    {
        ArgumentNullException.ThrowIfNull(stringEncoder);
        _stringEncoder = stringEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode]);
        _stringEncoder.Encode(writer, instruction.Operand.Text!);
    }
}
