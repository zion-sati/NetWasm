using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class VirtualDispatchCallEmitter(
    ITargetLayout layouts,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    IRuntimeIntrinsicRegistry intrinsics,
    IImplicitExceptionEmitter exceptions,
    IEnumEqualsEmitter enumEquals,
    IEnumHashCodeEmitter enumHashCode,
    IEnumCompareToEmitter enumCompareTo,
    IEnumTypeCodeEmitter enumTypeCode,
    IEnumValueReturnEmitter enumValueReturn,
    IEnumHasFlagEmitter enumHasFlag,
    IEnumToStringEmitter enumToString,
    IEnumConvertEmitter enumConvert,
    IValueTypeEqualsEmitter valueTypeEquals,
    IValueTypeHashCodeEmitter valueTypeHashCode,
    IConstrainedReferenceReceiverEmitter constrainedReceivers) :
    ICallEmitter
{
    public void Emit(
        CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        var dispatch = ResolveDispatchSite(request);
        var instruction = request.Instruction;
        var signature = request.Method.Signature;
        var receiverLocal = constrainedReceivers.Emit(
                instruction,
                code,
                request.ArgumentBase,
                request.ConstrainedType) ??
            GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                CliValueKind.ManagedReference);
        EmitNullCheck(code, receiverLocal);
        var returnsValue = signature.ReturnSignatureType.StackKind == CliValueKind.ValueType;
        var returnOffset = returnsValue
            ? instruction.Context.ValueLayout.TemporaryOffsets[
                instruction.Instruction.Offset]
            : 0;
        foreach (var target in dispatch.Targets)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiverLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(target.ReceiverType).TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            var intrinsic = intrinsics.TryGetIntrinsic(
                target.Method.Definition.Key,
                out var value)
                ? value
                : (RuntimeIntrinsic?)null;
            if (intrinsic == RuntimeIntrinsic.EnumEquals)
            {
                EmitEnumEquals(instruction, code, request.ArgumentBase, enumEquals);
            }
            else if (intrinsic == RuntimeIntrinsic.ValueTypeEquals)
            {
                valueTypeEquals.Emit(
                    new RuntimeIntrinsicEmissionRequest(
                        request,
                        intrinsic.Value,
                        target.ReceiverType,
                        layouts.Target,
                        functionIndices),
                    code,
                    target.ReceiverType,
                    CliValueKind.ManagedReference);
            }
            else if (intrinsic == RuntimeIntrinsic.ValueTypeGetHashCode)
            {
                valueTypeHashCode.Emit(
                    new RuntimeIntrinsicEmissionRequest(
                        request,
                        intrinsic.Value,
                        target.ReceiverType,
                        layouts.Target,
                        functionIndices),
                    code,
                    target.ReceiverType,
                    CliValueKind.ManagedReference);
            }
            else if (intrinsic == RuntimeIntrinsic.EnumGetHashCode)
            {
                EmitEnumHashCode(instruction, code, request.ArgumentBase, enumHashCode);
            }
            else if (intrinsic == RuntimeIntrinsic.EnumCompareTo)
            {
                EmitEnumCompareTo(instruction, code, request.ArgumentBase, enumCompareTo);
            }
            else if (intrinsic == RuntimeIntrinsic.EnumGetTypeCode)
            {
                EmitEnumTypeCode(
                    instruction,
                    code,
                    target,
                    request.ArgumentBase,
                    request.Consumed,
                    enumTypeCode,
                    enumValueReturn,
                    functionIndices);
            }
            else if (intrinsic == RuntimeIntrinsic.EnumHasFlag)
            {
                EmitEnumHasFlag(instruction, code, request.ArgumentBase, enumHasFlag);
            }
            else if (intrinsic == RuntimeIntrinsic.EnumToString)
            {
                EmitEnumToString(
                    instruction,
                    code,
                    target,
                    request.ArgumentBase,
                    request.Consumed,
                    enumToString,
                    functionIndices);
            }
            else if (intrinsic == RuntimeIntrinsic.EnumConvert)
            {
                EmitEnumConvert(
                    instruction,
                    code,
                    target,
                    request.ArgumentBase,
                    request.Consumed,
                    enumConvert,
                    functionIndices);
            }
            else
            {
                EmitTargetCall(
                    instruction, code, target,
                    receiverLocal,
                    request.ArgumentBase,
                    request.Consumed,
                    returnsValue,
                    returnOffset,
                    signature.ReturnType,
                    functionIndices);
            }
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        }
        exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        for (var index = 0; index < dispatch.Targets.Length; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (returnsValue)
        {
            EmitValueFrameAddress(
                code,
                instruction.Context,
                returnOffset);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                request.ArgumentBase,
                CliValueKind.ValueType)))));
            instruction.Stack.Add(CliValueKind.ValueType);
        }
        else if (signature.ReturnType != CliValueKind.Void)
        {
            instruction.Stack.Add(signature.ReturnType);
        }
    }

    private static DispatchCallSiteModel ResolveDispatchSite(CallEmissionRequest request)
    {
        if (request.Instruction.Instruction.Operation != CilOperation.CallVirtual)
        {
            throw new InvalidOperationException(
                "Virtual dispatch emission requires a callvirt instruction.");
        }

        var caller = request.Instruction.Header.MethodInstance?.CanonicalName;
        if (caller is null)
        {
            throw new InvalidOperationException(
                "Virtual dispatch emission requires the caller method identity.");
        }

        var key = $"{caller}@{request.Instruction.Instruction.Offset:x8}";
        if (!request.Instruction.Target.DispatchCallSites.TryGetValue(key, out var dispatch))
        {
            throw new InvalidOperationException(
                $"Virtual dispatch emission requires the planned call site '{key}'.");
        }

        return dispatch;
    }

    private void EmitTargetCall(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        DispatchTargetModel target,
        int receiverLocal,
        int argumentBase,
        int consumed,
        bool returnsValue,
        int returnOffset,
        CliValueKind returnType,
        IFunctionIndexResolver functionIndices)
    {
        if (returnsValue)
        {
            EmitValueFrameAddress(code, instruction.Context, returnOffset);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(receiverLocal))));
        if (target.ReceiverType.IsValueType && target.Method.DeclaringType.IsValueType)
        {
            var receiver = values.GetValueLayout(target.ReceiverType);
            var payloadOffset = WasmTargetLayout.Align(
                layouts.Target.ObjectHeaderSize,
                receiver.Alignment);
            EmitAddressConstant(code, payloadOffset);
            EmitAddressAdd(code);
        }
        for (var index = 1; index < consumed; index++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                argumentBase + index,
                instruction.Stack[argumentBase + index])))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(functionIndices.Resolve(target.Method)))));
        if (!returnsValue && returnType != CliValueKind.Void)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                instruction.Context,
                argumentBase,
                returnType)))));
        }
    }

    private void EmitEnumEquals(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        int argumentBase,
        IEnumEqualsEmitter emitter)
    {
        var left = GetStackLocal(
            instruction.Context,
            argumentBase,
            CliValueKind.ManagedReference);
        var right = GetStackLocal(
            instruction.Context,
            argumentBase + 1,
            CliValueKind.ManagedReference);
        var result = GetStackLocal(instruction.Context, argumentBase, CliValueKind.I4);
        emitter.EmitEquals(
            code,
            CliValueKind.ManagedReference,
            null,
            left,
            right,
            result,
            instruction.Context.ObjectTemporary,
            instruction.Context.NumericTemporaryI4);
    }

    private void EmitEnumHashCode(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        int argumentBase,
        IEnumHashCodeEmitter emitter)
    {
        var value = GetStackLocal(
            instruction.Context,
            argumentBase,
            CliValueKind.ManagedReference);
        var result = GetStackLocal(instruction.Context, argumentBase, CliValueKind.I4);
        emitter.EmitHashCode(
            code,
            CliValueKind.ManagedReference,
            null,
            value,
            result,
            instruction.Context.NumericTemporaryI4,
            instruction.Context.NumericTemporaryI8);
    }

    private void EmitEnumCompareTo(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        int argumentBase,
        IEnumCompareToEmitter emitter)
    {
        var left = GetStackLocal(
            instruction.Context,
            argumentBase,
            CliValueKind.ManagedReference);
        var right = GetStackLocal(
            instruction.Context,
            argumentBase + 1,
            CliValueKind.ManagedReference);
        var result = GetStackLocal(instruction.Context, argumentBase, CliValueKind.I4);
        emitter.EmitCompareTo(
            code,
            CliValueKind.ManagedReference,
            null,
            left,
            right,
            result,
            instruction.Context.ObjectTemporary,
            instruction.Context.NumericTemporaryI4);
    }

    private void EmitEnumTypeCode(
        InstructionEmissionRequest instruction,
        IWasmInstructionWriter code,
        DispatchTargetModel target,
        int argumentBase,
        int consumed,
        IEnumTypeCodeEmitter emitter,
        IEnumValueReturnEmitter valueReturns,
        IFunctionIndexResolver functionIndices)
    {
        var receiver = GetStackLocal(
            instruction.Context, argumentBase, CliValueKind.ManagedReference);
        var request = new RuntimeIntrinsicEmissionRequest(
            new CallEmissionRequest(
                instruction,
                target.Method,
                argumentBase,
                consumed),
            RuntimeIntrinsic.EnumGetTypeCode,
            null,
            layouts.Target,
            functionIndices);
        var valueTypeResult = target.Method.Signature.ReturnType == CliValueKind.ValueType;
        var result = valueTypeResult
            ? instruction.Context.NumericTemporaryI4
            : GetStackLocal(instruction.Context, argumentBase, CliValueKind.I4);
        emitter.EmitTypeCode(
            code, receiver, result, instruction.Context.NumericTemporaryI4);
        if (valueTypeResult)
            valueReturns.Emit(code, request, result);
    }

    private void EmitEnumHasFlag(
        InstructionEmissionRequest instruction, IWasmInstructionWriter code,
        int argumentBase, IEnumHasFlagEmitter emitter)
    {
        var receiver = GetStackLocal(
            instruction.Context, argumentBase, CliValueKind.ManagedReference);
        var flag = GetStackLocal(
            instruction.Context, argumentBase + 1, CliValueKind.ManagedReference);
        var result = GetStackLocal(
            instruction.Context, argumentBase, CliValueKind.I4);
        emitter.EmitHasFlag(
            code, receiver, flag, result, instruction.Context.NumericTemporaryI4);
    }

    private void EmitEnumToString(
        InstructionEmissionRequest instruction,
        IWasmInstructionWriter code,
        DispatchTargetModel target,
        int argumentBase,
        int consumed,
        IEnumToStringEmitter emitter,
        IFunctionIndexResolver functionIndices)
    {
        emitter.EmitToString(
            new RuntimeIntrinsicEmissionRequest(
                new CallEmissionRequest(
                    instruction,
                    target.Method,
                    argumentBase,
                    consumed),
                RuntimeIntrinsic.EnumToString,
                null,
                layouts.Target,
                functionIndices),
            code);
    }

    private void EmitEnumConvert(
        InstructionEmissionRequest instruction,
        IWasmInstructionWriter code,
        DispatchTargetModel target,
        int argumentBase,
        int consumed,
        IEnumConvertEmitter emitter,
        IFunctionIndexResolver functionIndices)
    {
        emitter.EmitConvert(
            new RuntimeIntrinsicEmissionRequest(
                new CallEmissionRequest(
                    instruction,
                    target.Method,
                    argumentBase,
                    consumed),
                RuntimeIntrinsic.EnumConvert,
                null,
                layouts.Target,
                functionIndices),
            code);
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
        EmitAddressConstant(code, offset);
        EmitAddressAdd(code);
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.WithOperand(WasmOpcodes.I64Constant, WasmInstructionOperand.Signed64(value)));
        else code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
    }

    private void EmitAddressAdd(IWasmInstructionWriter code)
    {
        if (layouts.Target.UsesMemory64) code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        else code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
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
