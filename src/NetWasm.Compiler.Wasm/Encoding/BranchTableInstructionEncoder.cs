// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed class BranchTableInstructionEncoder(
    IUnsignedLeb128Encoder operandEncoder) : IWasmInstructionEncoder
{
    private readonly IUnsignedLeb128Encoder _operandEncoder = operandEncoder ??
        throw new ArgumentNullException(nameof(operandEncoder));

    public void Encode(IWasmBinaryWriter writer, WasmInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(instruction);

        writer.Write([instruction.Opcode]);
        _operandEncoder.Encode(
            writer,
            checked((uint)instruction.Operand.BranchTableTargets.Length));
        foreach (var target in instruction.Operand.BranchTableTargets)
        {
            _operandEncoder.Encode(writer, target);
        }

        _operandEncoder.Encode(writer, instruction.Operand.BranchTableDefault);
    }
}
