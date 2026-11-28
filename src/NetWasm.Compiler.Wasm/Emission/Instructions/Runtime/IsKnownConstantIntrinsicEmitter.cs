// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// RuntimeHelpers.IsKnownConstant whose argument constant folding did not know as a constant, as .NET's JIT answers
// for one it does not see as a constant: false. A known one was folded to true before emission.
internal sealed class IsKnownConstantIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
    }
}
