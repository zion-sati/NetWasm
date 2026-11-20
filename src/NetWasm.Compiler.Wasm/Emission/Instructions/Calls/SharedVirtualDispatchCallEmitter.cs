// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// A virtual call with many targets (SharedVirtualDispatch) calls its declaration's shared dispatch function, which
// carries the one copy of the dispatch: the receiver checked for null here, then a value-type result's address, the
// receiver and the arguments, as the targets take them.
internal sealed class SharedVirtualDispatchCallEmitter(
    ITargetLayout layouts,
    IImplicitExceptionEmitter exceptions,
    IConstrainedReferenceReceiverEmitter constrainedReceivers) : ICallEmitter
{
    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var instruction = request.Instruction;
        var site = instruction.Target.DispatchCallSites.Required(
            $"{instruction.Header.MethodInstance!.CanonicalName}@{instruction.Instruction.Offset:x8}");
        var shared = instruction.Target.FunctionIndices.SharedVirtualDispatches[site.Declaration.CanonicalName];
        var context = instruction.Context;
        var receiverLocal = constrainedReceivers.Emit(instruction, code, request.ArgumentBase, request.ConstrainedType) ??
            StackLocal(context, request.ArgumentBase, CliValueKind.ManagedReference);
        if (!ReceiverNullProof.Proves(instruction))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiverLocal)));
            code.Write(WasmInstruction.NoOperand(layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.NullReference, context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        var signature = request.Method.Signature;
        var returnsValue = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType;
        var returnOffset = returnsValue ? context.ValueLayout.TemporaryOffsets[instruction.Instruction.Offset] : 0;
        if (returnsValue)
            EmitValueFrameAddress(code, context, returnOffset);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        for (var index = 1; index < request.Consumed; index++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)StackLocal(
                context, request.ArgumentBase + index, instruction.Stack[request.ArgumentBase + index]))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)shared.Value)));
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (returnsValue)
        {
            EmitValueFrameAddress(code, context, returnOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)StackLocal(
                context, request.ArgumentBase, CliValueKind.ValueType))));
            instruction.Stack.Add(CliValueKind.ValueType);
        }
        else if (signature.ReturnType != CliValueKind.Void)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)StackLocal(
                context, request.ArgumentBase, signature.ReturnType))));
            instruction.Stack.Add(signature.ReturnType);
        }
    }

    private void EmitValueFrameAddress(IWasmInstructionWriter code, MethodEmissionContext context, int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)context.ValueFrame)));
        if (offset == 0)
            return;
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(offset)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(offset)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private int StackLocal(MethodEmissionContext context, int slot, CliValueKind type) =>
        WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals, slot, type, layouts.Target);
}
