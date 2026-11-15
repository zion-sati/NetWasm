// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ExceptionPayloadBlockEmitter(ITargetLayout layouts) :
    IExceptionPayloadBlockEmitter
{
    public void Emit(IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Block,
            WasmInstructionOperand.BlockType(
                layouts.Target.UsesMemory64
                    ? (byte)WasmValueType.I64
                    : (byte)WasmValueType.I32)));
    }
}
