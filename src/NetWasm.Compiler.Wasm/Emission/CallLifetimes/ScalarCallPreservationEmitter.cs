// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal sealed class ScalarCallPreservationEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter addresses) : IScalarCallPreservationEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IValueFrameAddressEmitter _addresses = addresses ?? throw new ArgumentNullException(nameof(addresses));

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, ScalarCallPreservationPhase phase)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        if (phase is not (ScalarCallPreservationPhase.BeforeCall or ScalarCallPreservationPhase.AfterNormalReturn))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }
        var layout = request.Context.ValueLayout;
        if (!layout.ScalarCallLifetimes.CallLocals.TryGetValue(request.Instruction.Offset, out var locals))
        {
            return;
        }
        // Validate the complete selected transfer before emitting any instruction.
        foreach (var local in locals)
        {
            if ((uint)local >= (uint)request.Header.LocalSignatureTypes.Length ||
                request.Header.LocalSignatureTypes[local].StackKind is not (CliValueKind.F4 or CliValueKind.F8 or CliValueKind.I8) ||
                !layout.ScalarCallOffsets.TryGetValue(local, out var offset) || offset < 0 ||
                checked(offset + _layouts.Target.GetStorageSize(request.Header.LocalSignatureTypes[local])) > layout.Size ||
                layout.SpilledScalarLocals.Contains(local))
            {
                throw new InvalidOperationException("Scalar call preservation requires reserved storage and a register-backed local.");
            }
        }
        foreach (var local in locals)
        {
            var type = request.Header.LocalSignatureTypes[local];
            var size = _layouts.Target.GetStorageSize(type);
            var wasmLocal = checked(request.Context.LocalBase + local);
            _addresses.Emit(code, request.Context, layout.ScalarCallOffsets[local]);
            if (phase == ScalarCallPreservationPhase.BeforeCall)
            {
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                    WasmInstructionOperand.Unsigned((uint)wasmLocal)));
                ManagedMemoryEmitter.EmitStoreByType(code, _layouts.Target, 0, type, size);
            }
            else
            {
                ManagedMemoryEmitter.EmitLoadByType(code, _layouts.Target, 0, type, size);
                code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)wasmLocal)));
            }
        }
    }
}
