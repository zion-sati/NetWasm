// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Rethrows through the module's shared rethrow function, which keeps the captured stack
// trace, so the calling method holds no throw instruction.
internal sealed class ExceptionDispatchPreservedIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code)
    {
        var rethrowFunction = request.Instruction.Target.FunctionIndices
            .ManagedRethrowFunctionIndex.Value;
        var exceptionLocal = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)rethrowFunction)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }
}
