// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// Pushes the zero of a lane's value kind.
internal static class ZeroValues
{
    public static void Emit(IWasmInstructionWriter code, CliValueKind kind, WasmTargetLayout target)
    {
        switch (WasmValueTypes.FromCli(kind, target))
        {
            case var type when type == WasmValueTypes.FromCli(CliValueKind.I8, target) && type != WasmValueTypes.FromCli(CliValueKind.I4, target):
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)));
                break;
            case var type when type == WasmValueTypes.FromCli(CliValueKind.F4, target):
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.F32Constant, WasmInstructionOperand.Float32(0)));
                break;
            case var type when type == WasmValueTypes.FromCli(CliValueKind.F8, target):
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.F64Constant, WasmInstructionOperand.Float64(0)));
                break;
            case var type when type == WasmValueTypes.FromCli(CliValueKind.V128, target):
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.SimdPrefixed, WasmInstructionOperand.FromBytes([(byte)WasmSimdOpcodes.V128Constant, .. new byte[16]])));
                break;
            default:
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
                break;
        }
    }
}
