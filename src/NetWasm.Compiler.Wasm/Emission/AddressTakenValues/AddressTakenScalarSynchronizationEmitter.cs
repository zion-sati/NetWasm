// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed class AddressTakenScalarSynchronizationEmitter(
    ITargetLayout layouts) : IAddressTakenScalarSynchronizationEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        AddressTakenScalarCallSynchronization synchronization,
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
            var type = request.Header.LocalSignatureTypes[localIndex];
            var shadowLocal = request.Context.AddressTakenScalarPromotions
                .ShadowLocals[localIndex];
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)shadowLocal)));
            if (phase == AddressTakenValueSynchronizationPhase.BeforeCall)
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned(
                        (uint)(request.Context.LocalBase + localIndex))));
                ManagedMemoryEmitter.EmitStoreByType(
                    code,
                    _layouts.Target,
                    0,
                    type,
                    _layouts.Target.GetStorageSize(type));
            }
            else
            {
                ManagedMemoryEmitter.EmitLoadByType(
                    code,
                    _layouts.Target,
                    0,
                    type,
                    _layouts.Target.GetStorageSize(type));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned(
                        (uint)(request.Context.LocalBase + localIndex))));
            }
        }
    }
}
