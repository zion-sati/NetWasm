// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// The callback's new handle is the import's argument; nothing on the managed side keeps it.
internal sealed class TransferredHostCallbackEmitter(
    IRuntimeImportResolver runtimeImports) : ITransferredHostCallbackEmitter
{
    public void Emit(IWasmInstructionWriter code, int delegateLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)delegateLocal)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.HandleNew))));
    }
}
