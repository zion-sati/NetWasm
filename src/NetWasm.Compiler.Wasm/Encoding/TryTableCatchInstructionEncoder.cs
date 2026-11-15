// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class TryTableCatchInstructionEncoder : IWasmInstructionEncoder
{
    private readonly IUnsignedLeb128Encoder _operandEncoder;

    public TryTableCatchInstructionEncoder(IUnsignedLeb128Encoder operandEncoder)
    {
        ArgumentNullException.ThrowIfNull(operandEncoder);
        _operandEncoder = operandEncoder;
    }

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);
        writer.Write([instruction.Opcode, instruction.Operand.BlockTypeValue]);
        writer.Write([1, instruction.Operand.ByteValue]);
        if (instruction.Operand.ByteValue == 0)
            _operandEncoder.Encode(writer, instruction.Operand.TagIndex);
        _operandEncoder.Encode(writer, instruction.Operand.LabelDepth);
    }
}
