// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class AddressTakenAggregateSynchronizationEmitter(
    ITargetLayout layouts) : IAddressTakenAggregateSynchronizationEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        AddressTakenAggregateCallSynchronization synchronization,
        AddressTakenValueSynchronizationPhase phase)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(synchronization);

        var locals = phase == AddressTakenValueSynchronizationPhase.BeforeCall
            ? synchronization.LocalIndices
            : synchronization.ReloadLocalIndices;
        foreach (var localIndex in locals)
        {
            Emit(request, code, localIndex, phase);
        }
    }

    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int localIndex,
        AddressTakenValueSynchronizationPhase phase)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var localPlan = request.Context.AddressTakenAggregatePromotions.Plan
            .PromotedLocals[localIndex];
        var storageLocals = request.Context.AddressTakenAggregatePromotions
            .StorageLocals[localIndex];
        for (var storageIndex = 0;
             storageIndex < storageLocals.Length;
             storageIndex++)
        {
            var slot = localPlan.StorageSlots[storageIndex];
            WriteLocalGet(code, request.Context.LocalBase + localIndex);
            if (phase == AddressTakenValueSynchronizationPhase.BeforeCall)
            {
                WriteLocalGet(code, storageLocals[storageIndex]);
                ManagedMemoryEmitter.EmitStoreBySize(
                    code,
                    _layouts.Target,
                    slot.ByteOffset,
                    slot.Size);
            }
            else
            {
                ManagedMemoryEmitter.EmitLoadBySize(
                    code,
                    _layouts.Target,
                    slot.ByteOffset,
                    slot.Size);
                WriteLocalSet(code, storageLocals[storageIndex]);
            }
        }
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));

    private static void WriteLocalSet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
}
