// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class DelegateInvokeCallEmitter(
    ITargetLayout layouts,
    IImplicitExceptionEmitter exceptions,
    IValueTypeArgumentConventionFactory argumentConventions,
    IValueTypeResultConventionFactory resultConventions) : ICallEmitter
{
    public void Emit(
        CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var instruction = request.Instruction;
        var signature = request.Method.Signature;
        var delegateLocal = GetStackLocal(
            instruction.Context,
            request.ArgumentBase,
            CliValueKind.ManagedReference);
        EmitNullCheck(code, delegateLocal, request.Instruction.Context.ManagedThrowFunction);
        var returnsValue = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType;
        var lanes = instruction.Context.CallResultLanes.Calls.ContainsKey(instruction.Instruction.Offset);
        var returnOffset = returnsValue && !lanes
            ? instruction.Context.ValueLayout.TemporaryOffsets[
                instruction.Instruction.Offset]
            : 0;
        void EmitCall(IWasmInstructionWriter code)
        {
            for (var index = 0; index < request.Consumed; index++)
            {
                var local = GetStackLocal(instruction.Context, request.ArgumentBase + index, instruction.Stack[request.ArgumentBase + index]);
                argumentConventions.Create(StructParameters.Parameter(signature, false, index)).Emit(code,
                    writer => WriteLocalGet(writer, local), new(instruction.Context, instruction.Instruction.Offset, index,
                        instruction.Context.VectorScratch));
            }
            WriteCall(
                code,
                instruction.Target.FunctionIndices.GetDelegateInvokeHelper(
                    request.Method.DeclaringType.CanonicalName).Value);
        }
        // The invoke helper returns as the delegate's Invoke signature does.
        if (returnsValue)
        {
            resultConventions.Create(signature.ReturnSignatureType).Emit(code,
                writer => EmitValueFrameAddress(writer, instruction.Context, returnOffset), new(instruction.Context.StructResultLocals,
                    instruction.Context.VectorScratch, lanes),
                EmitCall);
        }
        else
        {
            EmitCall(code);
        }
        if (!returnsValue && signature.ReturnType != CliValueKind.Void)
        {
            WriteLocalSet(code, GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                signature.ReturnType));
        }
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (returnsValue && lanes)
        {
            instruction.Stack.Add(CliValueKind.ValueType);
        }
        else if (returnsValue)
        {
            EmitValueFrameAddress(code, instruction.Context, returnOffset);
            WriteLocalSet(code, GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                CliValueKind.ValueType));
            instruction.Stack.Add(CliValueKind.ValueType);
        }
        else if (signature.ReturnType != CliValueKind.Void)
        {
            instruction.Stack.Add(signature.ReturnType);
        }
    }

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        WriteLocalGet(code, objectLocal);
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitValueFrameAddress(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        WriteLocalGet(code, context.ValueFrame);
        if (offset == 0)
        {
            return;
        }
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

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);

    private static void WriteLocalGet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)local))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int local) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)local))));

    private static void WriteCall(IWasmInstructionWriter code, int function) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(checked((uint)function))));
}
