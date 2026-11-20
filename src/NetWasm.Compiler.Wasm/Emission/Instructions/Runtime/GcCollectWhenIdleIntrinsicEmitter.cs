// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// GCCollectionRuntime.CollectWhenIdle: the WASI event loop asks the runtime for the collection the heap is heading for, when it has grown enough to be worth it, and gets its answer. Only a program
// whose event loop asks imports the function, which follows the imports a module may leave out, so its index
// depends on the module's selection.
internal sealed class GcCollectWhenIdleIntrinsicEmitter(IRuntimeImportResolver runtimeImports)
    : IRuntimeIntrinsicEmitter
{
    private readonly IRuntimeImportResolver _runtimeImports = runtimeImports ??
        throw new ArgumentNullException(nameof(runtimeImports));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_runtimeImports.Resolve(
                RuntimeImportSymbol.CollectWhenIdle,
                request.Instruction.Target.RuntimeImportSelection))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Local(0, CliValueKind.I4))));
    }
}
