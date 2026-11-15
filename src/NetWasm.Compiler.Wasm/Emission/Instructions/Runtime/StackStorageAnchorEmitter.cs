// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Command: update backing storage only after the CIL operation succeeds.
internal sealed class StackStorageAnchorEmitter(
    ITargetLayout layouts,
    IValueFrameAddressEmitter addresses,
    IStackStorageAnchorValueEmitter values) : IStackStorageAnchorEmitter
{
    public void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        var context = request.Context;
        if (!context.RootMap.AnchorPlan.Assignments.TryGetValue(request.Instruction.Offset, out var assignments)) return;
        foreach (var assignment in assignments)
        {
            ValidateStorage(assignment.Destination);
            switch (assignment)
            {
                case StackStorageAnchorAssignment.Capture capture:
                    if ((uint)capture.EvaluationStackSlot >= (uint)request.Stack.Count ||
                        request.Stack[capture.EvaluationStackSlot] is not (CliValueKind.ValueType or CliValueKind.ManagedAddress))
                        throw new InvalidOperationException("A backing capture has no address-valued result.");
                    break;
                case StackStorageAnchorAssignment.Copy copy:
                    ValidateStorage(copy.Source);
                    break;
                case StackStorageAnchorAssignment.Clear:
                    break;
                default:
                    throw new InvalidOperationException("Unknown backing-anchor assignment.");
            }
        }
        foreach (var assignment in assignments)
        {
            if (assignment is StackStorageAnchorAssignment.Copy copyToSelf &&
                copyToSelf.Source == copyToSelf.Destination) continue;
            var shared = context.FilterEnvironment.AnchorOffsets.TryGetValue(assignment.Destination, out var offset);
            if (shared) addresses.Emit(code, context, offset);
            switch (assignment)
            {
                case StackStorageAnchorAssignment.Capture capture:
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                        WasmInstructionOperand.Unsigned(checked((uint)WasmLocalLayoutPlanner.GetEvaluationStackLocal(
                            context.StackLocals, capture.EvaluationStackSlot, request.Stack[capture.EvaluationStackSlot], layouts.Target)))));
                    break;
                case StackStorageAnchorAssignment.Copy copy:
                    values.Emit(code, context, copy.Source);
                    break;
                case StackStorageAnchorAssignment.Clear:
                    ManagedMemoryEmitter.EmitAddressConstant(code, layouts.Target, 0);
                    break;
            }
            if (shared) ManagedMemoryEmitter.EmitStoreBySize(code, layouts.Target, 0, layouts.Target.AddressSize);
            else code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned(checked((uint)context.AnchorLocals[assignment.Destination]))));
        }

        void ValidateStorage(int anchor)
        {
            if ((uint)anchor >= (uint)context.RootMap.AnchorPlan.Anchors.Length ||
                !context.AnchorLocals.ContainsKey(anchor) && !context.FilterEnvironment.AnchorOffsets.ContainsKey(anchor))
                throw new InvalidOperationException("A backing assignment has no physical storage.");
        }
    }
}
