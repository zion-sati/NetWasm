// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// GC.Collect(0) under a collector with generations asks the runtime for a collection of the youngest generation
// alone. Only a program that asks imports the function, which follows the imports a module may leave out, so its
// index depends on the module's selection.
internal sealed class GcCollectYoungIntrinsicEmitter(IRuntimeImportResolver runtimeImports) : IRuntimeIntrinsicEmitter
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
                RuntimeImportSymbol.GcCollectYoung,
                request.Instruction.Target.RuntimeImportSelection))));
    }
}
