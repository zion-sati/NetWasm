// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// A component's instantiation readies the runtime state only. The Component Model traps any wait
// there ("cannot block a synchronous task before returning"), so the module initializers, which
// may write to the console or read a sidecar, run in the first entry instead.
internal sealed class InstantiationStateInitializer : IInstantiationStateInitializer
{
    public void Initialize(
        GeneratedFunctionWriterLease code,
        RuntimeInitializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(plan);
        var index = plan.StateInitializerFunctionIndex ?? throw new InvalidOperationException(
            "Instantiation requires the module's state initializer function.");
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)index)));
    }
}
