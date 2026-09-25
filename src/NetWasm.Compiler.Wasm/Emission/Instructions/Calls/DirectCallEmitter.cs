using System;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class DirectCallEmitter(
    ITargetLayout layouts,
    IValueLayoutProvider values,
    IImplicitExceptionEmitter exceptions,
    IStaticInitializationEmitter initialization) : ICallEmitter
{
    public void Emit(
        CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var instruction = request.Instruction;
        var target = request.Method.Definition;
        var signature = request.Method.Signature;
        if (target.IsStatic && target.Name != ".cctor")
        {
            initialization.Emit(new(target.DeclaringType, request.Method.DeclaringType,
                instruction.Target.ModuleData, IsStaticMethodCall: true), code, functionIndices);
        }
        var constrainedReferenceReceiver = !target.IsStatic &&
            request.ConstrainedType is { IsValueType: false };
        if (constrainedReferenceReceiver)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                instruction.Stack[request.ArgumentBase])))));
            ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, 0);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(instruction.Context.ObjectTemporary))));
        }
        if (instruction.Instruction.Operation == CilOperation.CallVirtual)
        {
            EmitNullCheck(
                code,
                constrainedReferenceReceiver
                    ? instruction.Context.ObjectTemporary
                    : GetStackLocal(
                        instruction.Context,
                        request.ArgumentBase,
                        instruction.Stack[request.ArgumentBase]));
        }
        var returnsValue = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType;
        var returnOffset = returnsValue
            ? instruction.Context.ValueLayout.TemporaryOffsets[
                instruction.Instruction.Offset]
            : 0;
        if (returnsValue)
        {
            EmitValueFrameAddress(code, instruction.Context, returnOffset);
        }
        var scalarValueReceiver = !target.IsStatic &&
            request.Method.DeclaringType.IsValueType &&
            request.Method.DeclaringType.StackKind != CliValueKind.ValueType &&
            instruction.Stack[request.ArgumentBase] is not (
                CliValueKind.ManagedAddress or CliValueKind.NativeInt);
        if (scalarValueReceiver)
        {
            var receiverOffset = instruction.Context.ValueLayout.TemporaryOffsets[
                instruction.Instruction.Offset];
            EmitValueFrameAddress(code, instruction.Context, receiverOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                instruction.Stack[request.ArgumentBase])))));
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                request.Method.DeclaringType,
                values.GetValueLayout(request.Method.DeclaringType).Size);
        }
        for (var index = 0; index < request.Consumed; index++)
        {
            if (index == 0 && scalarValueReceiver)
            {
                EmitValueFrameAddress(
                    code,
                    instruction.Context,
                    instruction.Context.ValueLayout.TemporaryOffsets[
                        instruction.Instruction.Offset]);
            }
            else
            {
                if (index == 0 && constrainedReferenceReceiver)
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(instruction.Context.ObjectTemporary))));
                }
                else
                {
                    code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                        instruction.Context,
                        request.ArgumentBase + index,
                        instruction.Stack[request.ArgumentBase + index])))));
                }
            }
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(request.Method)))));
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (returnsValue)
        {
            EmitValueFrameAddress(code, instruction.Context, returnOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                CliValueKind.ValueType)))));
            instruction.Stack.Add(CliValueKind.ValueType);
        }
        else if (signature.ReturnType != CliValueKind.Void)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                signature.ReturnType)))));
            instruction.Stack.Add(signature.ReturnType);
        }
    }

    private void EmitNullCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64EqualZero));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitValueFrameAddress(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(context.ValueFrame))));
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
}
