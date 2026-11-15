// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class StructuredConditionValueEmitter(ITargetLayout layouts) : IStructuredConditionValueEmitter
{
    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context, ConditionValue condition)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                context.StackLocals, condition.Slot, condition.Type, layouts.Target))));
        if (condition.Type is CliValueKind.ManagedReference or CliValueKind.ManagedAddress)
        {
            code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
                ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
        else if (condition.Type == CliValueKind.I8 ||
                 condition.Type == CliValueKind.NativeInt && layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        }
    }
}
