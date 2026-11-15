// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedAggregateValueInitializer(
    ITargetLayout layouts) : IManagedAggregateValueInitializer
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public bool TryInitializeArgument(
        IWasmInstructionWriter code,
        StructuredMethodHeader header,
        MethodEmissionContext context,
        int argumentIndex)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(context);

        var storage = new ManagedAggregateStorageId(
            ManagedAggregateStorageKind.Argument,
            argumentIndex);
        if (!context.ManagedAggregateValues.Plan.Storages.TryGetValue(storage, out var selected))
        {
            return false;
        }
        if (!context.ValueLayout.ArgumentOffsets.ContainsKey(argumentIndex))
        {
            throw new InvalidOperationException(
                "A selected aggregate argument has no stable value-frame backing.");
        }

        var sourceLocal = checked(argumentIndex + context.ParameterOffset);
        foreach (var lane in context.ManagedAggregateValues.Plan.Lanes
                     .Where(lane => lane.Storage == storage)
                     .OrderBy(lane => lane.ByteOffset))
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)sourceLocal)));
            ManagedMemoryEmitter.EmitLoadBySize(
                code,
                _layouts.Target,
                lane.ByteOffset,
                selected.Lane(lane.ByteOffset).Size);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)context
                    .ManagedAggregateValues.LaneLocals[lane])));
        }
        return true;
    }
}
