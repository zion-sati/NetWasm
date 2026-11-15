// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Resolves an instruction encoding Strategy by its immutable operand shape.
/// </summary>
public interface IWasmInstructionEncoderRegistry
{
    IWasmInstructionEncoder Resolve(WasmInstructionOperandShape shape);
}
