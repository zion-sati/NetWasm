// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedAggregateValueReloadEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter valueFrameAddresses) : IManagedAggregateValueReloadEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueFrameAddressEmitter _valueFrameAddresses = valueFrameAddresses ??
        throw new ArgumentNullException(nameof(valueFrameAddresses));

    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);

        foreach (var materialization in request.Context.ManagedAggregateValues.Plan.Materializations
                     .Where(item => item.Materialization.InstructionOffset == request.Instruction.Offset &&
                         item.Materialization.ReloadAfter))
        {
            var storage = materialization.Materialization.Storage;
            var offsets = storage.Kind == ManagedAggregateStorageKind.Argument
                ? request.Context.ValueLayout.ArgumentOffsets
                : request.Context.ValueLayout.LocalOffsets;
            if (!offsets.TryGetValue(storage.Index, out var storageOffset) ||
                materialization.Reloads.IsEmpty)
            {
                throw new InvalidOperationException(
                    "A selected aggregate reload requires backing storage and lane definitions.");
            }
            foreach (var value in materialization.Reloads)
            {
                _valueFrameAddresses.Emit(code, request.Context,
                    checked(storageOffset + value.Lane.ByteOffset));
                ManagedMemoryEmitter.EmitLoadBySize(code, _layouts.Target, 0,
                    request.Context.ManagedAggregateValues.Plan.Storages[storage].Lane(value.Lane.ByteOffset).Size);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)request.Context
                        .ManagedAggregateValues.LaneLocals[value.Lane])));
            }
        }
    }
}
