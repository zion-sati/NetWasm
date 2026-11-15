// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class UnsafeIsAddressGreaterThanIntrinsicEmitter(ITargetLayout layouts) :
    IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.ManagedAddress)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Local(1, CliValueKind.ManagedAddress)))));
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64GreaterThanUnsigned));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.I4)))));
    }
}
