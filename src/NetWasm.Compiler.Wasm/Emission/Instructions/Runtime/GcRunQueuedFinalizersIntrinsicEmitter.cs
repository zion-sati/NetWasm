// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// GCFinalizerRuntime.RunQueued: runs the finalizers a collection has queued, through the runtime's finalizer safepoint,
// and yields the exception one of them let escape, or null. A program with no finalizable type queues none, so it
// yields null without calling the runtime.
internal sealed class GcRunQueuedFinalizersIntrinsicEmitter(IRuntimeImportResolver runtimeImports)
    : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var selection = request.Instruction.Target.RuntimeImportSelection;
        if (selection.IncludeFinalization)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                    RuntimeImportSymbol.FinalizerSafepoint,
                    selection))));
        }
        else if (request.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(0)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        }

        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.ManagedReference))));
    }
}
