// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class ManagedByRefAggregateInitializationEmitter(
    ITargetLayout layouts) : IManagedByRefAggregateInitializationEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public void Emit(IWasmInstructionWriter code, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var parameter in context.ManagedByRefAggregatePromotions.Plan
                     .PromotedParameters.OrderBy(pair => pair.Key))
        {
            var sourceLocal = context.ArgumentLocal(parameter.Key);
            var storageLocals = context.ManagedByRefAggregatePromotions.StorageLocals[
                parameter.Key];
            for (var index = 0; index < parameter.Value.StorageSlots.Length; index++)
            {
                var storage = parameter.Value.StorageSlots[index];
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)sourceLocal)));
                ManagedMemoryEmitter.EmitLoadBySize(
                    code,
                    _layouts.Target,
                    storage.ByteOffset,
                    storage.Size);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)storageLocals[index])));
            }
        }
    }
}
